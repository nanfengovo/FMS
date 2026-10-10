using System.Text.Json;
using Fms.Application;
using Fms.Domain.Identity;
using Fms.EntityFrameworkCore;
using Fms.HttpApi.Host.Authorization;
using Fms.HttpApi.Host.Replay;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fms.HttpApi.Host.Controllers;

[ApiController]
[Route("api/fms/replay")]
public class ReplayController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly FmsDbContext _db;
    private readonly FmsAppService _app;
    private readonly ReplaySceneService _scenes;

    public ReplayController(FmsDbContext db, FmsAppService app, ReplaySceneService scenes)
    {
        _db = db;
        _app = app;
        _scenes = scenes;
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.ReplayScenes.AsNoTracking()
            .OrderByDescending(scene => scene.CreatedAt)
            .Take(40)
            .Select(scene => new
            {
                scene.Id,
                scene.Name,
                scene.MapName,
                scene.TraceId,
                scene.VehicleId,
                scene.Status,
                scene.StartSimMs,
                scene.EndSimMs,
                scene.SampleCount,
                scene.CreatedAt
            })
            .ToListAsync();
        return Ok(new { success = true, items });
    }

    [RequiresPermission(FmsPermissions.Simulation)]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var scene = await _db.ReplayScenes.AsNoTracking()
            .Where(item => item.Id == id || item.TraceId == id)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync();
        if (scene == null) return NotFound(new { error = "没有这段回放" });
        return Ok(new
        {
            success = true,
            scene = new
            {
                scene.Id,
                scene.Name,
                scene.MapName,
                scene.TraceId,
                scene.VehicleId,
                scene.Status,
                scene.StartSimMs,
                scene.EndSimMs,
                scene.SampleCount,
                scene.CreatedAt,
                frames = JsonSerializer.Deserialize<List<ReplaySampleDto>>(scene.FramesJson, JsonOptions) ?? new(),
                logs = JsonSerializer.Deserialize<List<ReplayLogDto>>(scene.LogsJson, JsonOptions) ?? new()
            }
        });
    }

    [RequiresPermission(FmsPermissions.SimulationControl)]
    [HttpPost("capture")]
    public async Task<IActionResult> Capture([FromBody] CaptureReplayRequest? request)
    {
        var scene = await _scenes.CaptureAsync(_db, _app, request?.TraceId, request?.Seconds ?? 45);
        return Ok(new { success = true, id = scene.Id, name = scene.Name, sampleCount = scene.SampleCount });
    }
}

public class CaptureReplayRequest
{
    public string? TraceId { get; set; }
    public int Seconds { get; set; } = 45;
}
