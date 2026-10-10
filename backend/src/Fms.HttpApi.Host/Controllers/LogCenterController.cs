using Fms.Application.Logging;
using Fms.Domain.Identity;
using Fms.Domain.Logging;
using Fms.HttpApi.Host.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/logs")]
public class LogCenterController : ControllerBase
{
    private readonly ILogCenterService _logs;

    public LogCenterController(ILogCenterService logs)
    {
        _logs = logs;
    }

    [RequiresPermission(FmsPermissions.Logs)]
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? category,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? source,
        [FromQuery] string? level,
        [FromQuery] string? taskSerial,
        [FromQuery] string? vehicleId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _logs.QueryAsync(
            category,
            keyword,
            from,
            to,
            page,
            pageSize,
            cancellationToken,
            source,
            level,
            taskSerial,
            vehicleId);
        return Ok(new { code = "0000", data = result });
    }

    [RequiresPermission(FmsPermissions.Logs)]
    [HttpGet("text")]
    public async Task<IActionResult> Text(
        [FromQuery] string? category,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? source,
        [FromQuery] string? level,
        [FromQuery] string? taskSerial,
        [FromQuery] string? vehicleId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _logs.QueryAsync(
            category,
            keyword,
            from,
            to,
            page,
            pageSize,
            cancellationToken,
            source,
            level,
            taskSerial,
            vehicleId);
        return Ok(new { code = "0000", data = new { result.Total, lines = result.Lines } });
    }

    [RequiresPermission(FmsPermissions.Logs)]
    [HttpGet("wiki")]
    public async Task<IActionResult> Wiki(CancellationToken cancellationToken)
    {
        var pages = await _logs.GetWikiAsync(cancellationToken);
        return Ok(new { code = "0000", data = pages });
    }

    [RequiresPermission(FmsPermissions.LogsAnalyze)]
    [HttpPut("wiki/{id}")]
    public async Task<IActionResult> SaveWiki(string id, [FromBody] WikiInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest(new { code = "4000", msg = "标题和正文不能为空" });
        await _logs.SaveWikiAsync(id, input.Title, input.Body, cancellationToken);
        return Ok(new { code = "0000", msg = "Wiki 已保存" });
    }

    [RequiresPermission(FmsPermissions.LogsSettings)]
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var settings = await _logs.GetSettingsAsync(cancellationToken);
        return Ok(new { code = "0000", data = settings });
    }

    [RequiresPermission(FmsPermissions.LogsSettings)]
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] AiEndpointInput input, CancellationToken cancellationToken)
    {
        await _logs.SaveSettingsAsync(input, cancellationToken);
        return Ok(new { code = "0000", msg = "接口密钥已保存" });
    }

    [RequiresPermission(FmsPermissions.LogsSettings)]
    [HttpPost("settings/test")]
    public async Task<IActionResult> TestSettings([FromBody] AiEndpointInput? input, CancellationToken cancellationToken)
    {
        var testResult = await _logs.TestEndpointAsync(input, cancellationToken);
        return Ok(new { code = "0000", data = testResult });
    }

    [RequiresPermission(FmsPermissions.LogsSettings)]
    [HttpPost("settings/models")]
    public async Task<IActionResult> ListModels([FromBody] AiEndpointInput? input, CancellationToken cancellationToken)
    {
        var result = await _logs.ListAvailableModelsAsync(input, cancellationToken);
        return Ok(new { code = "0000", data = result });
    }

    [RequiresPermission(FmsPermissions.LogsAnalyze)]
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return BadRequest(new { code = "4000", msg = "请输入要分析的问题" });
        var result = await _logs.AnalyzeAsync(input.Question, input.Mode ?? "rag", cancellationToken);
        return Ok(new { code = "0000", data = result });
    }

    public sealed class WikiInput
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }

    public sealed class AnalyzeInput
    {
        public string Question { get; set; } = "";
        public string? Mode { get; set; }
    }
}
