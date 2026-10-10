using Fms.Domain.Identity;
using Fms.HttpApi.Host.Authorization;
using Fms.Application.Logging;
using Fms.Domain.Logging;
using Fms.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/fms/logs")]
public class LoggingController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;
    private readonly IFlightRecorderService _flightRecorderService;
    private readonly IDecisionLogService _decisionLogService;

    public LoggingController(
        IAuditLogService auditLogService,
        IFlightRecorderService flightRecorderService,
        IDecisionLogService decisionLogService)
    {
        _auditLogService = auditLogService;
        _flightRecorderService = flightRecorderService;
        _decisionLogService = decisionLogService;
    }

    /// <summary>
    /// 查询调度决策诊断日志 (Track 2: 排障追踪与决策链溯源)
    /// </summary>
    [RequiresPermission(FmsPermissions.SystemAudit)]
    [HttpGet("decisions")]
    public IActionResult GetDecisionLogs(
        [FromQuery] string? category,
        [FromQuery] string? subjectId,
        [FromQuery] int limit = 100)
    {
        DecisionCategory? parsedCategory = null;
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<DecisionCategory>(category, true, out var c))
        {
            parsedCategory = c;
        }

        var (items, total) = _decisionLogService.Query(parsedCategory, subjectId, limit);
        return Ok(new
        {
            code = "0000",
            msg = "查询成功",
            data = new
            {
                items,
                total,
                limit
            }
        });
    }

    /// <summary>
    /// 查询操作审计日志
    /// </summary>
    [RequiresPermission(FmsPermissions.SystemAudit)]
    [HttpGet("audit")]
    public IActionResult GetAuditLogs(
        [FromQuery] string? action,
        [FromQuery] string? @operator,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (items, total) = _auditLogService.Query(action, @operator, from, to, page, pageSize);
        return Ok(new
        {
            code = "0000",
            msg = "查询成功",
            data = new
            {
                items,
                total,
                page,
                pageSize
            }
        });
    }

    /// <summary>
    /// 获取黑匣子高频遥测回放帧
    /// </summary>
    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("flight-recorder/recent")]
    public IActionResult GetRecentFrames([FromQuery] string? vehicleId, [FromQuery] int limit = 500)
    {
        var frames = _flightRecorderService.GetRecentFrames(vehicleId, limit);
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                totalInRingBuffer = _flightRecorderService.FrameCount,
                frames
            }
        });
    }

    /// <summary>
    /// 获取指定时间窗内的黑匣子帧（用于事故与死锁精确复盘）
    /// </summary>
    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("flight-recorder/range")]
    public IActionResult GetFramesRange([FromQuery] long startMs, [FromQuery] long endMs, [FromQuery] string? vehicleId)
    {
        var frames = _flightRecorderService.GetFramesRange(startMs, endMs, vehicleId);
        return Ok(new
        {
            code = "0000",
            msg = "获取成功",
            data = new
            {
                startMs,
                endMs,
                count = frames.Count,
                frames
            }
        });
    }

    /// <summary>
    /// 清理黑匣子缓冲区
    /// </summary>
    [Authorize]
    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("flight-recorder/clear")]
    public IActionResult ClearRecorder()
    {
        _flightRecorderService.Clear();
        return Ok(new { code = "0000", msg = "黑匣子缓冲区已清理" });
    }
}
