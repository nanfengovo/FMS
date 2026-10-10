using System.Text.Json;
using Fms.Application;
using Microsoft.EntityFrameworkCore;
using Fms.Application.Logging;
using Fms.Domain.Logging;
using Fms.Domain.Workflow;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;

namespace Fms.HttpApi.Host.Replay;

public sealed class ReplaySampleDto
{
    public long T { get; set; }
    public List<ReplayVehicleDto> Vehicles { get; set; } = new();
}

public sealed class ReplayVehicleDto
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Th { get; set; }
    public string St { get; set; } = "Idle";
}

public sealed class ReplayLogDto
{
    public long SimMs { get; set; }
    public string Line { get; set; } = string.Empty;
}

public class ReplaySceneService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IFlightRecorderService _recorder;

    public ReplaySceneService(IFlightRecorderService recorder)
    {
        _recorder = recorder;
    }

    public async Task ArchiveFinishedAsync(FmsAppService app, FmsDbContext db, CancellationToken cancellationToken = default)
    {
        var now = app.Engine.Clock.CurrentTimestampMs;
        foreach (var instance in app.Engine.WorkflowManager.GetAllInstances())
        {
            if (!IsTerminal(instance.Status) || instance.ReplaySaved) continue;
            if (instance.EndSimMs == 0)
            {
                instance.EndSimMs = now;
                continue;
            }

            if (await db.ReplayScenes.AnyAsync(scene => scene.TraceId == instance.InstanceId, cancellationToken))
            {
                instance.ReplaySaved = true;
                continue;
            }

            await SaveAsync(db, app, instance.InstanceId, instance.StartSimMs, instance.EndSimMs, instance, cancellationToken);
            instance.ReplaySaved = true;
        }
    }

    public async Task<ReplaySceneRecord> CaptureAsync(
        FmsDbContext db,
        FmsAppService app,
        string? traceId,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        var now = app.Engine.Clock.CurrentTimestampMs;
        var window = Math.Clamp(seconds, 5, 180) * 1000L;
        WorkflowInstance? instance = null;
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            instance = app.Engine.WorkflowManager.GetInstance(traceId);
        }

        var start = instance is { StartSimMs: > 0 } ? instance.StartSimMs : now - window;
        var end = instance is { EndSimMs: > 0 } ? instance.EndSimMs : now;
        return await SaveAsync(db, app, traceId, start, end, instance, cancellationToken);
    }

    private async Task<ReplaySceneRecord> SaveAsync(
        FmsDbContext db,
        FmsAppService app,
        string? traceId,
        long start,
        long end,
        WorkflowInstance? instance,
        CancellationToken cancellationToken)
    {
        if (end < start) end = start;
        var frames = _recorder.GetFramesRange(start, end);
        var samples = Downsample(frames, 720);
        var definition = instance == null ? null : app.Engine.WorkflowManager.GetDefinition(instance.DefinitionId);
        var record = new ReplaySceneRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = definition?.Name ?? "仿真片段",
            MapName = app.Engine.Graph.Name,
            TraceId = traceId ?? string.Empty,
            VehicleId = instance?.BindVehicleId ?? string.Empty,
            Status = instance?.Status.ToString() ?? "Captured",
            StartSimMs = samples.Count > 0 ? samples[0].T : start,
            EndSimMs = samples.Count > 0 ? samples[^1].T : end,
            SampleCount = samples.Count,
            CreatedAt = DateTime.UtcNow,
            FramesJson = JsonSerializer.Serialize(samples, JsonOptions),
            LogsJson = JsonSerializer.Serialize(
                instance?.LogMarks.Select(mark => new ReplayLogDto { SimMs = mark.SimMs, Line = mark.Line }).ToList()
                ?? new List<ReplayLogDto>(),
                JsonOptions)
        };
        db.ReplayScenes.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public static List<ReplaySampleDto> Downsample(IReadOnlyList<TelemetrySnapshotFrame> frames, int maxSamples)
    {
        if (frames.Count == 0) return new();
        var start = frames[0].TimestampMs;
        var end = frames[^1].TimestampMs;
        var bucket = Math.Max(200, (end - start) / Math.Max(1, maxSamples));
        var grouped = new SortedDictionary<long, Dictionary<string, TelemetrySnapshotFrame>>();
        foreach (var frame in frames)
        {
            var key = start + ((frame.TimestampMs - start) / bucket) * bucket;
            if (!grouped.TryGetValue(key, out var vehicles))
            {
                vehicles = new Dictionary<string, TelemetrySnapshotFrame>(StringComparer.OrdinalIgnoreCase);
                grouped[key] = vehicles;
            }
            vehicles[frame.VehicleId] = frame;
        }

        return grouped.Select(pair => new ReplaySampleDto
        {
            T = pair.Key,
            Vehicles = pair.Value.Values.Select(frame => new ReplayVehicleDto
            {
                Id = frame.VehicleId,
                X = Math.Round(frame.Pose.X, 3),
                Y = Math.Round(frame.Pose.Y, 3),
                Th = Math.Round(frame.Pose.ThetaRad, 4),
                St = frame.Status
            }).ToList()
        }).ToList();
    }

    public static bool IsTerminal(WorkflowInstanceStatus status) =>
        status is WorkflowInstanceStatus.Completed
            or WorkflowInstanceStatus.Fault
            or WorkflowInstanceStatus.Cancelled
            or WorkflowInstanceStatus.Terminated;
}
