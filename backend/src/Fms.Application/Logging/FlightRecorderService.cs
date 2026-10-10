using System.Collections.Concurrent;
using System.Threading.Channels;
using Fms.Domain.Logging;
using Fms.Domain.Vehicles;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Logging;

public interface IFlightRecorderService
{
    void RecordFrame(VehicleAggregate vehicle, long timestampMs);
    IReadOnlyList<TelemetrySnapshotFrame> GetRecentFrames(string? vehicleId = null, int limit = 500);
    IReadOnlyList<TelemetrySnapshotFrame> GetFramesRange(long startMs, long endMs, string? vehicleId = null);
    void Clear();
    int FrameCount { get; }
}

public class FlightRecorderService : IFlightRecorderService, IDisposable
{
    private readonly Channel<TelemetrySnapshotFrame> _channel;
    private readonly ConcurrentQueue<TelemetrySnapshotFrame> _ringBuffer = new();
    private readonly int _maxCapacity;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;
    private readonly ILogger<FlightRecorderService>? _logger;

    public int FrameCount => _ringBuffer.Count;

    public FlightRecorderService(int maxCapacity = 50000, ILogger<FlightRecorderService>? logger = null)
    {
        _maxCapacity = maxCapacity;
        _logger = logger;
        _channel = Channel.CreateBounded<TelemetrySnapshotFrame>(new BoundedChannelOptions(_maxCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        _consumerTask = Task.Run(ProcessQueueAsync);
    }

    public void RecordFrame(VehicleAggregate vehicle, long timestampMs)
    {
        var frame = new TelemetrySnapshotFrame
        {
            TimestampMs = timestampMs,
            VehicleId = vehicle.Id,
            Pose = vehicle.Pose,
            Speed = vehicle.CurrentSpeed,
            AngularVelocity = vehicle.CurrentAngularVelocity,
            BatteryPercent = vehicle.Bms.SocPercent,
            Status = vehicle.Status.ToString(),
            CurrentPathId = vehicle.CurrentPathId,
            CurrentNodeId = vehicle.CurrentNodeId,
            AssignedOrderId = vehicle.AssignedOrderId,
            SafetyLaserTriggered = vehicle.IsEstop
        };

        _channel.Writer.TryWrite(frame);
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;
        while (!_cts.Token.IsCancellationRequested && await reader.WaitToReadAsync(_cts.Token))
        {
            while (reader.TryRead(out var frame))
            {
                _ringBuffer.Enqueue(frame);
                while (_ringBuffer.Count > _maxCapacity && _ringBuffer.TryDequeue(out _))
                {
                    // keep ring buffer bounded
                }
            }
        }
    }

    public IReadOnlyList<TelemetrySnapshotFrame> GetRecentFrames(string? vehicleId = null, int limit = 500)
    {
        var query = _ringBuffer.AsEnumerable();
        if (!string.IsNullOrEmpty(vehicleId))
        {
            query = query.Where(f => f.VehicleId == vehicleId);
        }
        return query.TakeLast(limit).ToList();
    }

    public IReadOnlyList<TelemetrySnapshotFrame> GetFramesRange(long startMs, long endMs, string? vehicleId = null)
    {
        var query = _ringBuffer.Where(f => f.TimestampMs >= startMs && f.TimestampMs <= endMs);
        if (!string.IsNullOrEmpty(vehicleId))
        {
            query = query.Where(f => f.VehicleId == vehicleId);
        }
        return query.OrderBy(f => f.TimestampMs).ToList();
    }

    public void Clear()
    {
        while (_ringBuffer.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
