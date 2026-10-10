using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Adapters;

public record VehicleTelemetry(
    string VehicleId,
    Pose2D Pose,
    double Speed,
    double AngularVelocity,
    double BatterySoc,
    VehicleStatus Status,
    string? CurrentPathId,
    string? CurrentTaskId,
    List<string> ErrorCodes,
    long TimestampMs
);

public interface IVehicleCommAdapter
{
    string AdapterType { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    Task SendTrajectoryAsync(string vehicleId, List<Point2D> trajectoryPoints, double maxSpeed, CancellationToken ct = default);
    Task CancelTaskAsync(string vehicleId, string taskId, CancellationToken ct = default);
    Task SendInstantActionAsync(string vehicleId, ActionType action, string optionCode, CancellationToken ct = default);

    event EventHandler<VehicleTelemetry>? TelemetryReceived;
}
