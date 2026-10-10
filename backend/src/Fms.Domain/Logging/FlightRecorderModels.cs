using System.Collections.Generic;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Logging;

public class TelemetrySnapshotFrame
{
    public long TimestampMs { get; set; }
    public string VehicleId { get; set; } = string.Empty;
    public Pose2D Pose { get; set; } = Pose2D.Zero;
    public double Speed { get; set; }
    public double AngularVelocity { get; set; }
    public double BatteryPercent { get; set; }
    public string Status { get; set; } = "Idle";
    public string? CurrentPathId { get; set; }
    public string? CurrentNodeId { get; set; }
    public string? AssignedOrderId { get; set; }
    public bool SafetyLaserTriggered { get; set; }
    public List<string> HoldingMutexZoneIds { get; set; } = new();
}

public class FlightRecorderSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Flight Session";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public int MaxBufferSize { get; set; } = 50000;
}
