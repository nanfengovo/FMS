using Fms.Domain.Vehicles;
using Fms.Engine;

namespace Fms.HttpApi.Host.Realtime;

/// <summary>
/// 车队台账 + 实时遥测投影。列表、详情、SignalR 批次共用同一份字段。
/// </summary>
public static class VehicleSnapshotBuilder
{
    public static object Describe(DispatchEngine engine, VehicleAggregate v)
    {
        bool presenceOnline = v.IsEnabled && v.IsOnline;
        string? offlineReason = !v.IsEnabled
            ? "disabled"
            : !v.IsOnline
                ? "heartbeat_timeout"
                : null;

        return new
        {
            id = v.Id,
            name = string.IsNullOrWhiteSpace(v.Name) ? v.Id : v.Name,
            code = string.IsNullOrWhiteSpace(v.Code) ? v.Id : v.Code,
            vehicleType = v.VehicleType,
            driveType = v.Kinematics.DriveType.ToString(),
            status = v.Status.ToString(),
            pose = new { x = v.Pose.X, y = v.Pose.Y, theta = v.Pose.ThetaRad, thetaDeg = v.Pose.ThetaDeg },
            x = Math.Round(v.Pose.X, 4),
            y = Math.Round(v.Pose.Y, 4),
            theta = Math.Round(v.Pose.ThetaDeg, 2),
            speed = Math.Round(v.CurrentSpeed, 3),
            currentSpeed = v.CurrentSpeed,
            currentAngularVelocity = v.CurrentAngularVelocity,
            battery = Math.Round(v.Bms.SocPercent, 1),
            batteryPercent = Math.Round(v.Bms.SocPercent, 1),
            batteryVoltage = Math.Round(v.Bms.VoltageV, 1),
            isCharging = v.Bms.IsCharging || v.Status == Fms.Domain.Shared.Enums.VehicleStatus.Charging,
            lowBatteryThreshold = v.Bms.LowBatteryThreshold,
            length = v.Kinematics.Length,
            width = v.Kinematics.Width,
            maxSpeed = v.Kinematics.MaxSpeed,
            currentPathId = v.CurrentPathId,
            currentNodeId = v.CurrentNodeId,
            nodeId = v.CurrentNodeId,
            nearestNodeId = v.CurrentNodeId
                ?? engine.Graph.FindNearestNode(v.Pose.Position, double.MaxValue)?.Id,
            targetNodeId = v.TargetNodeId,
            homeNodeId = v.HomeNodeId,
            assignedOrderId = v.AssignedOrderId,
            currentOrderId = v.AssignedOrderId,
            avoidance = v.CurrentAvoidanceStrategy.ToString(),
            blockedBy = engine.GetBlockerId(v),
            holdingPayload = v.HoldingPayload,
            payload = v.HoldingPayload,
            payloadCarrierId = v.PayloadCarrierId,
            actionPhase = v.ActionPhase,
            liftProgress = Math.Round(v.LiftProgress, 2),
            cargoType = v.CargoType,
            isEnabled = v.IsEnabled,
            isOnline = v.IsOnline,
            presenceOnline,
            offlineReason,
            lastHeartbeatUtc = v.LastHeartbeatUtc.ToString("O"),
            lastTelemetryUtc = v.LastHeartbeatUtc.ToString("O"),
            heartbeatTimeoutSec = DispatchEngine.HeartbeatTimeoutSeconds,
            mapId = v.MapId,
            mapName = v.MapName,
            dispatchEligible = DispatchEngine.IsDispatchEligible(v)
        };
    }

    public static object[] Build(DispatchEngine engine) =>
        engine.Vehicles.Values
            .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(v => Describe(engine, v))
            .ToArray();
}
