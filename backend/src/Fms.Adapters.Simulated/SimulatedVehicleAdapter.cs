using Fms.Domain.Adapters;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Vehicles;

namespace Fms.Adapters.Simulated;

public class SimulatedVehicleAdapter : IVehicleCommAdapter
{
    public string AdapterType => "Simulated-Kinematics";

    private readonly Dictionary<string, VehicleAggregate> _simVehicles = new();
    public event EventHandler<VehicleTelemetry>? TelemetryReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public void RegisterVehicle(VehicleAggregate vehicle)
    {
        _simVehicles[vehicle.Id] = vehicle;
    }

    public Task SendTrajectoryAsync(string vehicleId, List<Point2D> trajectoryPoints, double maxSpeed, CancellationToken ct = default)
    {
        if (_simVehicles.TryGetValue(vehicleId, out var vehicle))
        {
            vehicle.TrajectoryPoints = trajectoryPoints;
            vehicle.CurrentTrajectoryIndex = 0;
            vehicle.Kinematics.MaxSpeed = maxSpeed;
            vehicle.Status = VehicleStatus.Running;
        }
        return Task.CompletedTask;
    }

    public Task CancelTaskAsync(string vehicleId, string taskId, CancellationToken ct = default)
    {
        if (_simVehicles.TryGetValue(vehicleId, out var vehicle))
        {
            vehicle.ClearRoute();
        }
        return Task.CompletedTask;
    }

    public Task SendInstantActionAsync(string vehicleId, ActionType action, string optionCode, CancellationToken ct = default)
    {
        if (_simVehicles.TryGetValue(vehicleId, out var vehicle))
        {
            if (action == ActionType.Wait)
            {
                vehicle.Status = VehicleStatus.Paused;
            }
        }
        return Task.CompletedTask;
    }

    public void EmitTelemetry(VehicleAggregate v, long timestampMs)
    {
        var telemetry = new VehicleTelemetry(
            v.Id,
            v.Pose,
            v.CurrentSpeed,
            v.CurrentAngularVelocity,
            v.Bms.SocPercent,
            v.Status,
            v.CurrentPathId,
            v.AssignedOrderId,
            new(),
            timestampMs
        );
        TelemetryReceived?.Invoke(this, telemetry);
    }
}
