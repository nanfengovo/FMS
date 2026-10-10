using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Fms.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Rcs;

public class FmsTrafficConfig
{
    public double DeadlockTimeoutSec { get; set; } = 30.0;
    public AvoidanceStrategy DefaultAvoidanceStrategy { get; set; } = AvoidanceStrategy.PullOverSpur;
    public double BatteryLowThreshold { get; set; } = 0.20;
    public double MaxSpeedLimit { get; set; } = 2.0;
    public double CorridorSafetyBuffer { get; set; } = 0.5;
    public bool EmptyYieldLoaded { get; set; } = true;

    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (DeadlockTimeoutSec < 1.0 || DeadlockTimeoutSec > 600.0)
            return (false, "死锁超时阈值必须在 1.0 ~ 600.0 秒之间");
        if (BatteryLowThreshold < 0.05 || BatteryLowThreshold > 0.80)
            return (false, "低电量阈值必须在 5% ~ 80% 之间");
        if (MaxSpeedLimit <= 0.1 || MaxSpeedLimit > 5.0)
            return (false, "全局限速上限必须在 0.1 ~ 5.0 m/s 之间");
        if (CorridorSafetyBuffer < 0.1 || CorridorSafetyBuffer > 3.0)
            return (false, "通道安全间距缓冲区必须在 0.1 ~ 3.0 m 之间");

        return (true, null);
    }
}

public class RcsConfigurationService
{
    private readonly ILogger<RcsConfigurationService>? _logger;
    private string _configDir = AppContext.BaseDirectory;

    public string CustomIp { get; private set; } = "127.0.0.1";
    public int CustomPort { get; private set; } = 9000;
    public Dictionary<string, string> UrlMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string PushServerIp { get; private set; } = "127.0.0.1";
    public int PushServerPort { get; private set; } = 9000;
    public bool PushEnabled { get; private set; } = true;
    public string PushStatusUrl { get; private set; } = "/api/v1/xinsong/report_robot_status";

    public HashSet<string> EnabledControlTags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public FmsTrafficConfig TrafficConfig { get; } = new();
    public long TotalStatusPushed { get; private set; }
    public DateTime? LastStatusPushedUtc { get; private set; }

    public string BaseCustomUrl => $"http://{CustomIp}:{CustomPort}";
    public string BasePushUrl => $"http://{PushServerIp}:{PushServerPort}";

    private readonly List<Fms.Domain.Rcs.RcsProfile> _profiles = Fms.Domain.Rcs.RcsProfile.GetBuiltInProfiles();
    public Fms.Domain.Rcs.RcsProfile ActiveProfile { get; private set; }

    public IReadOnlyList<Fms.Domain.Rcs.RcsProfile> GetProfiles() => _profiles.AsReadOnly();

    public RcsConfigurationService(ILogger<RcsConfigurationService>? logger = null)
    {
        _logger = logger;
        ActiveProfile = _profiles.FirstOrDefault(p => p.IsActive) ?? _profiles[0];
        LoadConfigurations();
    }

    public void LoadConfigurations(string? searchDirectory = null)
    {
        string dir = searchDirectory ?? AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(dir, "custom.set")))
        {
            string currentDir = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(currentDir, "custom.set")))
            {
                dir = currentDir;
            }
        }
        _configDir = dir;

        LoadProfilesJson(Path.Combine(dir, "rcs_profiles.json"));
        LoadCustomSet(Path.Combine(dir, "custom.set"));
        LoadKcFmsXml(Path.Combine(dir, "kcfms.xml"));
        LoadControlTag(Path.Combine(dir, "control.tag"));
        LoadTrafficConfig(Path.Combine(dir, "traffic.json"));
    }

    public Fms.Domain.Rcs.RcsProfile SetActiveProfile(string profileId)
    {
        var target = _profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            foreach (var p in _profiles) p.IsActive = false;
            target.IsActive = true;
            ActiveProfile = target;

            if (Uri.TryCreate(target.RcsBaseUrl, UriKind.Absolute, out var uri))
            {
                CustomIp = uri.Host;
                CustomPort = uri.Port;
            }
            PushEnabled = target.PushEnabled;

            SaveProfilesJson();
            _logger?.LogInformation("[RcsConfig] Active Profile switched to {Name} ({Id})", target.Name, target.Id);
        }
        return ActiveProfile;
    }

    public void SaveProfile(Fms.Domain.Rcs.RcsProfile profile)
    {
        var existing = _profiles.FirstOrDefault(p => p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _profiles.Remove(existing);
        }
        _profiles.Add(profile);
        if (profile.IsActive)
        {
            SetActiveProfile(profile.Id);
        }
        else
        {
            SaveProfilesJson();
        }
    }

    private void LoadProfilesJson(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<Fms.Domain.Rcs.RcsProfile>>(File.ReadAllText(filePath));
            if (list != null && list.Count > 0)
            {
                _profiles.Clear();
                _profiles.AddRange(list);
                ActiveProfile = _profiles.FirstOrDefault(p => p.IsActive) ?? _profiles[0];
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load rcs_profiles.json");
        }
    }

    private void SaveProfilesJson()
    {
        try
        {
            string path = Path.Combine(_configDir, "rcs_profiles.json");
            File.WriteAllText(path, JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save rcs_profiles.json");
        }
    }

    private void LoadCustomSet(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger?.LogInformation("custom.set not found at {Path}, using defaults.", filePath);
            InitDefaultUrls();
            return;
        }

        try
        {
            string content = File.ReadAllText(filePath);
            if (!content.TrimStart().StartsWith("<Config>", StringComparison.OrdinalIgnoreCase))
            {
                content = $"<Config>{content}</Config>";
            }

            var doc = XDocument.Parse(content);
            var customElem = doc.Root?.Element("Custom");
            if (customElem != null)
            {
                CustomIp = customElem.Attribute("ip")?.Value ?? "127.0.0.1";
                if (int.TryParse(customElem.Attribute("port")?.Value, out int port))
                {
                    CustomPort = port;
                }
            }

            UrlMappings.Clear();
            foreach (var urlElem in doc.Root?.Elements("URL") ?? Enumerable.Empty<XElement>())
            {
                string? method = urlElem.Attribute("method")?.Value;
                string? url = urlElem.Attribute("url")?.Value;
                if (!string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(url))
                {
                    UrlMappings[method] = url;
                }
            }

            _logger?.LogInformation("Loaded custom.set: Custom={Ip}:{Port}, URLs count={Count}", CustomIp, CustomPort, UrlMappings.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse custom.set, falling back to defaults.");
            InitDefaultUrls();
        }
    }

    private void LoadKcFmsXml(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger?.LogInformation("kcfms.xml not found at {Path}, using defaults.", filePath);
            return;
        }

        try
        {
            string content = File.ReadAllText(filePath);
            if (!content.TrimStart().StartsWith("<Config>", StringComparison.OrdinalIgnoreCase) &&
                !content.TrimStart().StartsWith("<PushService", StringComparison.OrdinalIgnoreCase))
            {
                content = $"<Config>{content}</Config>";
            }

            var doc = XDocument.Parse(content);
            var pushElem = doc.Root?.Name == "PushService" ? doc.Root : doc.Root?.Element("PushService");
            if (pushElem != null)
            {
                PushServerIp = pushElem.Attribute("server")?.Value ?? "127.0.0.1";
                if (int.TryParse(pushElem.Attribute("port")?.Value, out int port))
                {
                    PushServerPort = port;
                }
                PushEnabled = pushElem.Attribute("enable")?.Value == "1";

                var methodElem = pushElem.Element("Method");
                if (methodElem != null && !string.IsNullOrEmpty(methodElem.Attribute("url")?.Value))
                {
                    PushStatusUrl = methodElem.Attribute("url")!.Value;
                }
            }

            _logger?.LogInformation("Loaded kcfms.xml: Push={Server}:{Port}, Enabled={Enabled}, Url={Url}",
                PushServerIp, PushServerPort, PushEnabled, PushStatusUrl);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse kcfms.xml, using defaults.");
        }
    }

    private void LoadControlTag(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            EnabledControlTags.Clear();
            foreach (var line in File.ReadAllLines(filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split('=');
                if (parts.Length == 2 && bool.TryParse(parts[1], out bool enabled) && enabled)
                {
                    EnabledControlTags.Add(parts[0].Trim());
                }
                else if (parts.Length == 1)
                {
                    EnabledControlTags.Add(parts[0].Trim());
                }
            }

            _logger?.LogInformation("Loaded control.tag: {Count} tags enabled.", EnabledControlTags.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read control.tag.");
        }
    }

    public void SaveCustomSet(string ip, int port, Dictionary<string, string>? urls = null)
    {
        CustomIp = ip;
        CustomPort = port;
        if (urls != null)
        {
            foreach (var kvp in urls)
            {
                UrlMappings[kvp.Key] = kvp.Value;
            }
        }

        var doc = new XDocument(
            new XElement("Config",
                new XElement("Custom", new XAttribute("ip", CustomIp), new XAttribute("port", CustomPort)),
                UrlMappings.Select(kv => new XElement("URL", new XAttribute("method", kv.Key), new XAttribute("url", kv.Value)))
            )
        );

        string path = Path.Combine(_configDir, "custom.set");
        doc.Save(path);
        _logger?.LogInformation("Hot-updated custom.set to {Path}", path);
    }

    public void SaveKcFmsXml(string server, int port, bool enable, string url)
    {
        PushServerIp = server;
        PushServerPort = port;
        PushEnabled = enable;
        PushStatusUrl = url;

        var doc = new XDocument(
            new XElement("PushService",
                new XAttribute("server", PushServerIp),
                new XAttribute("port", PushServerPort),
                new XAttribute("enable", PushEnabled ? "1" : "0"),
                new XElement("Method", new XAttribute("name", "custom"), new XAttribute("url", PushStatusUrl), new XAttribute("type", "POST"))
            )
        );

        string path = Path.Combine(_configDir, "kcfms.xml");
        doc.Save(path);
        _logger?.LogInformation("Hot-updated kcfms.xml to {Path}", path);
    }

    public void SaveControlTags(IEnumerable<string> tags)
    {
        EnabledControlTags.Clear();
        foreach (var t in tags) EnabledControlTags.Add(t);

        string path = Path.Combine(_configDir, "control.tag");
        File.WriteAllLines(path, EnabledControlTags.Select(t => $"{t}=true"));
        _logger?.LogInformation("Hot-updated control.tag to {Path}", path);
    }

    public void RecordStatusPush()
    {
        TotalStatusPushed++;
        LastStatusPushedUtc = DateTime.UtcNow;
    }

    public void ApplyTrafficConfig(FmsTrafficConfig config)
    {
        TrafficConfig.DeadlockTimeoutSec = config.DeadlockTimeoutSec;
        TrafficConfig.DefaultAvoidanceStrategy = config.DefaultAvoidanceStrategy;
        TrafficConfig.BatteryLowThreshold = config.BatteryLowThreshold;
        TrafficConfig.MaxSpeedLimit = config.MaxSpeedLimit;
        TrafficConfig.CorridorSafetyBuffer = config.CorridorSafetyBuffer;
        TrafficConfig.EmptyYieldLoaded = config.EmptyYieldLoaded;
        SaveTrafficConfig();
    }

    private void LoadTrafficConfig(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<FmsTrafficConfig>(File.ReadAllText(filePath), TrafficJsonOptions);
            if (loaded == null) return;
            TrafficConfig.DeadlockTimeoutSec = loaded.DeadlockTimeoutSec;
            TrafficConfig.DefaultAvoidanceStrategy = loaded.DefaultAvoidanceStrategy;
            TrafficConfig.BatteryLowThreshold = loaded.BatteryLowThreshold;
            TrafficConfig.MaxSpeedLimit = loaded.MaxSpeedLimit;
            TrafficConfig.CorridorSafetyBuffer = loaded.CorridorSafetyBuffer;
            TrafficConfig.EmptyYieldLoaded = loaded.EmptyYieldLoaded;
            _logger?.LogInformation("Loaded traffic.json from {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse traffic.json");
        }
    }

    private void SaveTrafficConfig()
    {
        string path = Path.Combine(_configDir, "traffic.json");
        File.WriteAllText(path, JsonSerializer.Serialize(TrafficConfig, TrafficJsonOptions));
        _logger?.LogInformation("Hot-updated traffic.json to {Path}", path);
    }

    private static readonly JsonSerializerOptions TrafficJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private void InitDefaultUrls()
    {
        UrlMappings["task_info"] = "/api/v1/xinsong/task_info";
        UrlMappings["task_arrive_target"] = "/api/v1/xinsong/task_arrive_target";
        UrlMappings["robot_permiss_start_action"] = "/api/v1/xinsong/robot_permiss_start_action";
        UrlMappings["check_cargo_rfid"] = "/api/v1/xinsong/check_cargo_rfid";
        UrlMappings["task_finish"] = "/api/v1/xinsong/task_finish";
        UrlMappings["task_delete"] = "/api/v1/xinsong/task_delete";
    }

    public string GetEndpointUrl(string method, string taskType = "")
    {
        string rawUrl = UrlMappings.TryGetValue(method, out var path) ? path : $"/api/v1/xinsong/{method}";

        string prefix = "";
        if (ActiveProfile.UseTypePrefixOnCallback && !string.IsNullOrEmpty(taskType) && taskType.Contains('_'))
        {
            string type = taskType.Split('_')[0];
            prefix = $"/{type}";
        }

        return $"{BaseCustomUrl}{prefix}{rawUrl}";
    }

    public string GetStatusReportUrl()
    {
        return $"{BasePushUrl}{PushStatusUrl}";
    }
}
