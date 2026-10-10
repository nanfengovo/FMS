using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fms.Domain.Logging;

public enum LogCategory
{
    UserOperation,
    Inbound,
    Outbound,
    Api,
    Runtime
}

public sealed class LogWrite
{
    public LogCategory Category { get; init; }
    public string Level { get; init; } = "Information";
    public string Source { get; init; } = "";
    public string Actor { get; init; } = "";
    public string Message { get; init; } = "";
    public string? PayloadJson { get; init; }
    public string? TraceId { get; init; }
    public string? Method { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public long? DurationMs { get; init; }
    public bool Success { get; init; } = true;
}

public interface ILogSink
{
    void Enqueue(LogWrite write);
}

public enum ApiKeyPlacement
{
    BearerHeader,
    Header,
    Query
}

public sealed record ApiKeyBinding(ApiKeyPlacement Placement, string Name, string Value);

public static class OpenApiKeyParser
{
    public static ApiKeyBinding Resolve(
        string apiKey,
        string? openApiJson,
        string? schemeName,
        string? headerName,
        string? queryName)
    {
        if (TryReadScheme(openApiJson, schemeName, out var placement, out var name))
            return new ApiKeyBinding(placement, name, apiKey);

        if (!string.IsNullOrWhiteSpace(queryName))
            return new ApiKeyBinding(ApiKeyPlacement.Query, queryName.Trim(), apiKey);

        if (!string.IsNullOrWhiteSpace(headerName) &&
            !headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            return new ApiKeyBinding(ApiKeyPlacement.Header, headerName.Trim(), apiKey);

        return new ApiKeyBinding(ApiKeyPlacement.BearerHeader, "Authorization", apiKey);
    }

    private static bool TryReadScheme(string? openApiJson, string? schemeName, out ApiKeyPlacement placement, out string name)
    {
        placement = ApiKeyPlacement.BearerHeader;
        name = "Authorization";
        if (string.IsNullOrWhiteSpace(openApiJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(openApiJson);
            if (!doc.RootElement.TryGetProperty("components", out var components) ||
                !components.TryGetProperty("securitySchemes", out var schemes) ||
                schemes.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement scheme = default;
            var found = false;
            if (!string.IsNullOrWhiteSpace(schemeName) && schemes.TryGetProperty(schemeName, out var named))
            {
                scheme = named;
                found = true;
            }
            else
            {
                foreach (var item in schemes.EnumerateObject())
                {
                    scheme = item.Value;
                    found = true;
                    break;
                }
            }

            if (!found) return false;
            var type = scheme.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            if (string.Equals(type, "http", StringComparison.OrdinalIgnoreCase))
            {
                placement = ApiKeyPlacement.BearerHeader;
                name = "Authorization";
                return true;
            }

            if (string.Equals(type, "apiKey", StringComparison.OrdinalIgnoreCase))
            {
                var where = scheme.TryGetProperty("in", out var inNode) ? inNode.GetString() : "header";
                name = scheme.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "X-API-Key" : "X-API-Key";
                placement = string.Equals(where, "query", StringComparison.OrdinalIgnoreCase)
                    ? ApiKeyPlacement.Query
                    : ApiKeyPlacement.Header;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}

public static class LogSqlGuard
{
    private static readonly string[] Forbidden =
    [
        "insert", "update", "delete", "drop", "alter", "attach", "pragma", "union", "into", "replace", "vacuum"
    ];

    public static bool TryAccept(string sql, out string safe, out string error)
    {
        safe = "";
        error = "";
        var text = (sql ?? "").Trim().TrimEnd(';').Trim();
        if (text.Length == 0)
        {
            error = "没有可执行的查询";
            return false;
        }

        if (text.Contains(';') || text.Contains("--") || text.Contains("/*") || text.Contains("*/"))
        {
            error = "只允许一条不带注释的 SELECT";
            return false;
        }

        if (!text.StartsWith("select", StringComparison.OrdinalIgnoreCase))
        {
            error = "只允许 SELECT";
            return false;
        }

        var lower = text.ToLowerInvariant();
        foreach (var word in Forbidden)
        {
            if (Regex.IsMatch(lower, $@"\b{word}\b"))
            {
                error = "查询包含不允许的语句";
                return false;
            }
        }

        if (!Regex.IsMatch(text, @"\bfrom\s+(Fms_LogEvents|Fms_LogWiki)\b", RegexOptions.IgnoreCase))
        {
            error = "只能查询 Fms_LogEvents 或 Fms_LogWiki";
            return false;
        }

        if (Regex.Matches(text, @"\bfrom\b", RegexOptions.IgnoreCase).Count != 1)
        {
            error = "只能包含一个 FROM";
            return false;
        }

        var limit = Regex.Match(text, @"\blimit\s+(\d+)\s*$", RegexOptions.IgnoreCase);
        if (!limit.Success)
        {
            text += " LIMIT 100";
        }
        else if (int.TryParse(limit.Groups[1].Value, out var count) && count > 200)
        {
            text = Regex.Replace(text, @"\blimit\s+\d+\s*$", "LIMIT 200", RegexOptions.IgnoreCase);
        }

        safe = text;
        return true;
    }
}

public static class LogText
{
    public static string KeywordSelect(string question)
    {
        var filters = new List<string>();
        var text = question ?? "";
        void Add(string pattern, string clause)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase)) filters.Add(clause);
        }

        Add("入站", "Category = 'Inbound'");
        Add("inbound", "Category = 'Inbound'");
        Add("出站", "Category = 'Outbound'");
        Add("outbound", "Category = 'Outbound'");
        Add("接口", "Category = 'Api'");
        Add("运行", "Category = 'Runtime'");
        Add("操作", "Category = 'UserOperation'");
        Add("错误", "Success = 0");
        Add("失败", "Success = 0");
        Add("error", "Level = 'Error'");

        var where = filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters.Distinct());
        return "SELECT Timestamp, Category, Level, Actor, Message, Path, StatusCode, DurationMs, Success FROM Fms_LogEvents" +
               where + " ORDER BY Timestamp DESC LIMIT 50";
    }

    public static string Format(
        DateTime timestamp,
        string category,
        string level,
        string source,
        string actor,
        string? method,
        string? path,
        int? statusCode,
        long? durationMs,
        string message)
    {
        var http = string.IsNullOrWhiteSpace(method) ? "" : $" {method} {path} {statusCode} {durationMs}ms";
        return $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {source} {actor}{http} {message}".Trim();
    }

    public static IReadOnlyList<T> Rank<T>(string question, IReadOnlyList<T> docs, Func<T, string> text, int take)
    {
        var tokens = Tokenize(question);
        if (tokens.Count == 0 || docs.Count == 0) return docs.Take(take).ToArray();

        return docs
            .Select(doc => (doc, score: Score(tokens, text(doc))))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .Take(take)
            .Select(item => item.doc)
            .ToArray();
    }

    public static List<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(token => token.Length >= 2)
            .Distinct()
            .ToList();
    }

    public static int Score(IReadOnlyList<string> tokens, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || tokens.Count == 0) return 0;
        var hay = text.ToLowerInvariant();
        return tokens.Count(token => hay.Contains(token, StringComparison.Ordinal));
    }
}

public static class LogCorrelation
{
    private static readonly Regex TaskKeyRegex = new(
        @"(?:task_serial|taskSerial|orderSerial|OrderSerial|任务流水号)\s*[""']?\s*[:=]\s*[""']?([A-Za-z0-9._-]{3,80})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VehicleKeyRegex = new(
        @"(?:vehicleId|VehicleId|AGV_serial|agv_serial|BindVehicleId|assignedVehicleId)\s*[""']?\s*[:=]\s*[""']?([A-Za-z0-9._-]{1,64})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NamedVehicleRegex = new(
        @"\b(?:AMR|AGV)[-_]?\d{1,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> NonVehicleActors = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "FMS", "anonymous", "scheduler", "Admin", "SYS_DAEMON", "VDA_BRIDGE",
        "SAFETY_AGENT", "DISPATCH_ENGINE", "Engineer", "Operator", "Viewer", "R_SUPER"
    };

    public static string[] LevelAliases(string? level)
    {
        var key = (level ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "info" or "information" => ["Information", "Info", "information", "info"],
            "warn" or "warning" => ["Warning", "Warn", "warning", "warn"],
            "error" => ["Error", "error"],
            "debug" => ["Debug", "debug"],
            "trace" => ["Trace", "trace"],
            "critical" or "fatal" => ["Critical", "Fatal", "critical", "fatal"],
            _ => string.IsNullOrWhiteSpace(level) ? [] : [level.Trim()]
        };
    }

    public static string? TaskSerialOf(string? traceId, string? actor, string? message, string? path, string? payloadJson)
    {
        var hay = Join(traceId, actor, message, path, payloadJson);
        var match = TaskKeyRegex.Match(hay);
        if (match.Success) return match.Groups[1].Value;
        if (!string.IsNullOrWhiteSpace(traceId) && !IsHttpTrace(traceId))
            return traceId;
        return null;
    }

    public static string? VehicleIdOf(string? traceId, string? actor, string? message, string? path, string? payloadJson)
    {
        var hay = Join(traceId, actor, message, path, payloadJson);
        var keyed = VehicleKeyRegex.Match(hay);
        if (keyed.Success)
        {
            var value = keyed.Groups[1].Value;
            if (!NonVehicleActors.Contains(value) && value is not "0" and not "auto")
                return value;
        }

        var named = NamedVehicleRegex.Match(hay);
        if (named.Success) return named.Value.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(actor) && LooksLikeVehicle(actor))
            return actor;
        return null;
    }

    public static bool LooksLikeVehicle(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || NonVehicleActors.Contains(actor)) return false;
        if (NamedVehicleRegex.IsMatch(actor)) return true;
        return Regex.IsMatch(actor, @"^\d{1,4}$") && actor != "0";
    }

    public static bool IsHttpTrace(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        (id.Contains(':') || id.StartsWith("0H", StringComparison.OrdinalIgnoreCase));

    private static string Join(params string?[] parts) =>
        string.Join('\n', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
