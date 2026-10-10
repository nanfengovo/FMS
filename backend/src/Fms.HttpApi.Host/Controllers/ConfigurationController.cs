using System.Net.Http.Json;
using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application.Logging;
using Fms.Application.Rcs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/fms/config")]
public class ConfigurationController : ControllerBase
{
    private readonly RcsConfigurationService _configService;
    private readonly IAuditLogService _auditLogService;

    public ConfigurationController(RcsConfigurationService configService, IAuditLogService auditLogService)
    {
        _configService = configService;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 获取当前生效的全部配置（通信、推送、遥测标签与交通调度策略）
    /// </summary>
    [RequiresPermission(FmsPermissions.System)]
    [HttpGet]
    public IActionResult GetAllConfig()
    {
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                customSet = new
                {
                    ip = _configService.CustomIp,
                    port = _configService.CustomPort,
                    urls = _configService.UrlMappings
                },
                kcFmsXml = new
                {
                    server = _configService.PushServerIp,
                    port = _configService.PushServerPort,
                    enable = _configService.PushEnabled,
                    url = _configService.PushStatusUrl
                },
                controlTags = _configService.EnabledControlTags,
                traffic = _configService.TrafficConfig,
                pushStats = new
                {
                    totalPushed = _configService.TotalStatusPushed,
                    lastPushedAt = _configService.LastStatusPushedUtc
                },
                activeProfile = _configService.ActiveProfile,
                profiles = _configService.GetProfiles()
            }
        });
    }

    /// <summary>
    /// 热更新 custom.set 通信配置
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.System)]
    [HttpPut("custom-set")]
    public IActionResult UpdateCustomSet([FromBody] UpdateCustomSetDto dto)
    {
        if (dto.Port <= 0 || dto.Port > 65535)
        {
            return BadRequest(new { code = "4000", msg = "端口号非法，必须位于 1-65535 之间" });
        }

        _configService.SaveCustomSet(dto.Ip, dto.Port, dto.Urls);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "custom.set", $"更新上位机通信地址为 {dto.Ip}:{dto.Port}");

        return Ok(new { code = "0000", msg = "custom.set 热更新成功" });
    }

    /// <summary>
    /// 热更新 kcfms.xml 状态推送配置
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.System)]
    [HttpPut("kcfms-xml")]
    public IActionResult UpdateKcFmsXml([FromBody] UpdateKcFmsXmlDto dto)
    {
        if (dto.Port <= 0 || dto.Port > 65535)
        {
            return BadRequest(new { code = "4000", msg = "端口号非法，必须位于 1-65535 之间" });
        }

        _configService.SaveKcFmsXml(dto.Server, dto.Port, dto.Enable, dto.Url);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "kcfms.xml", $"更新车辆推送为 {dto.Server}:{dto.Port}, 启用={dto.Enable}");

        return Ok(new { code = "0000", msg = "kcfms.xml 热更新成功" });
    }

    /// <summary>
    /// 热更新 control.tag 遥测过滤标签
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.SignalsWrite)]
    [HttpPut("control-tags")]
    public IActionResult UpdateControlTags([FromBody] List<string> tags)
    {
        _configService.SaveControlTags(tags);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "control.tag", $"更新控制标签列表，共 {tags.Count} 项");
        return Ok(new { code = "0000", msg = "control.tag 热更新成功" });
    }

    /// <summary>
    /// 探测北向回调：对配置的上位机地址发起真实 HTTP POST。
    /// </summary>
    [RequiresPermission(FmsPermissions.System)]
    [HttpPost("test-endpoint")]
    public async Task<IActionResult> TestEndpoint([FromBody] TestEndpointDto dto, [FromServices] IHttpClientFactory httpFactory)
    {
        string path = string.IsNullOrWhiteSpace(dto.Path) ? "/" : (dto.Path.StartsWith('/') ? dto.Path : "/" + dto.Path);
        string url = $"{_configService.BaseCustomUrl}{path}";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMilliseconds(dto.TimeoutMs > 0 ? dto.TimeoutMs : 3000);
            using var response = await client.PostAsJsonAsync(url, new { probe = true, method = dto.Key, timestamp = DateTime.UtcNow });
            string body = await response.Content.ReadAsStringAsync();
            if (body.Length > 400) body = body[..400];
            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                status = (int)response.StatusCode,
                rttMs = watch.ElapsedMilliseconds,
                url,
                body
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                success = false,
                status = 0,
                rttMs = watch.ElapsedMilliseconds,
                url,
                error = ex.GetBaseException().Message
            });
        }
    }

    /// <summary>
    /// 热更新交通与避让策略（严格防呆校验）
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.MapEdit)]
    [HttpPut("traffic")]
    public IActionResult UpdateTrafficConfig([FromBody] FmsTrafficConfig config)
    {
        var (isValid, error) = config.Validate();
        if (!isValid)
        {
            return BadRequest(new { code = "4000", msg = error });
        }

        _configService.ApplyTrafficConfig(config);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "TrafficConfig", $"更新交通策略: 死锁时限={config.DeadlockTimeoutSec}s, 限速={config.MaxSpeedLimit}m/s, 走廊缓冲={config.CorridorSafetyBuffer}m, 空车让重车={config.EmptyYieldLoaded}, 避让={config.DefaultAvoidanceStrategy}");

        return Ok(new { code = "0000", msg = "交通与避让策略热更新成功", data = _configService.TrafficConfig });
    }

    /// <summary>
    /// 获取全部 RCS 协议适配配置及当前激活 Profile
    /// </summary>
    [RequiresPermission(FmsPermissions.System)]
    [HttpGet("profiles")]
    public IActionResult GetProfiles()
    {
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                activeProfile = _configService.ActiveProfile,
                profiles = _configService.GetProfiles()
            }
        });
    }

    /// <summary>
    /// 切换激活的 RCS 协议配置 (Standard, TXC, NXP-TW, SanDisk-MT, NXP-TJ, 等)
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.System)]
    [HttpPut("profiles/active")]
    public IActionResult SetActiveProfile([FromBody] SetActiveProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ProfileId))
        {
            return BadRequest(new { code = "4000", msg = "profileId 不能为空" });
        }

        var active = _configService.SetActiveProfile(dto.ProfileId);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "RcsProfile", $"切换激活 RCS 协议配置为: {active.Name} ({active.Id})");
        return Ok(new { code = "0000", msg = $"已成功切换为 {active.Name}", data = active });
    }

    /// <summary>
    /// 保存或更新 RCS 协议配置 Profile
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.System)]
    [HttpPost("profiles")]
    public IActionResult SaveProfile([FromBody] Fms.Domain.Rcs.RcsProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
        {
            return BadRequest(new { code = "4000", msg = "Profile Id 和 Name 不能为空" });
        }

        _configService.SaveProfile(profile);
        _auditLogService.Log("Admin", "UPDATE_CONFIG", "RcsProfile", $"保存 RCS 协议配置: {profile.Name} ({profile.Id})");
        return Ok(new { code = "0000", msg = "保存 RCS 协议配置成功", data = _configService.ActiveProfile });
    }

    public record UpdateCustomSetDto(string Ip, int Port, Dictionary<string, string>? Urls);
    public record UpdateKcFmsXmlDto(string Server, int Port, bool Enable, string Url);
    public record TestEndpointDto(string Key, string Path, int TimeoutMs = 3000);
    public record SetActiveProfileDto(string ProfileId);
}
