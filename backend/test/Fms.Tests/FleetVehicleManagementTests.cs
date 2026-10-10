using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class FleetVehicleManagementTests
{
    private static DispatchEngine EngineWithMap()
    {
        var engine = new DispatchEngine();
        engine.Graph.Name = "Restored-Topology-Map";
        engine.Graph.AddNode(new MapNode("L1", new Point2D(0, 0), NodeType.Landmark, "L1"));
        engine.Graph.AddNode(new MapNode("L2", new Point2D(10, 0), NodeType.Landmark, "L2"));
        engine.Graph.AddNode(new MapNode("SB-1", new Point2D(1, 1), NodeType.StandbyPoint, "待命1"));
        engine.Graph.AddNode(new MapNode("CH-A", new Point2D(20, 20), NodeType.ChargePoint, "充电A"));
        engine.Graph.AddPath(new StraightLinePath("P1", "L1", "L2", new Point2D(0, 0), new Point2D(10, 0)));
        return engine;
    }

    [Fact]
    public void RegisterVehicle_SnapsEmptyCurrentNodeAndBindsMap()
    {
        var engine = EngineWithMap();
        var vehicle = new VehicleAggregate("BENCH-AMR-01", "BENCH-AMR-01", new Pose2D(0.2, 0.1, 0));
        engine.RegisterVehicle(vehicle);

        Assert.Equal("L1", vehicle.CurrentNodeId);
        Assert.Equal("SB-1", vehicle.HomeNodeId);
        Assert.Equal("Restored-Topology-Map", vehicle.MapId);
        Assert.Equal("Restored-Topology-Map", vehicle.MapName);
        Assert.True(vehicle.IsEnabled);
    }

    [Fact]
    public void DisableThenEnable_ChangesDispatchEligibility()
    {
        var engine = EngineWithMap();
        var vehicle = new VehicleAggregate("V1", "V1", new Pose2D(0, 0, 0)) { CurrentNodeId = "L1" };
        engine.RegisterVehicle(vehicle);
        Assert.True(DispatchEngine.IsDispatchEligible(vehicle));

        engine.SetVehicleEnabled("V1", false);
        var order = new TransportOrderAggregate("ORD-ELIG-1", 50);
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Action = ActionType.Move,
            TargetNodeId = "L2",
            Name = "去 L2"
        });
        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        Assert.False(DispatchEngine.IsDispatchEligible(vehicle));
        Assert.Equal(OrderStatus.Queued, order.Status);
        Assert.Null(order.AssignedVehicleId);

        engine.SetVehicleEnabled("V1", true);
        engine.AdvanceTick(0.1);

        Assert.Equal("V1", order.AssignedVehicleId);
        Assert.NotEqual(OrderStatus.Queued, order.Status);
    }

    [Fact]
    public void UpdateLedger_KeepsHomeNodeAndRejectsLandmark()
    {
        var engine = EngineWithMap();
        var vehicle = new VehicleAggregate("V1", "V1", new Pose2D(0, 0, 0));
        engine.RegisterVehicle(vehicle);

        Assert.True(engine.UpdateVehicleLedger(
            "V1",
            name: "AMR-测试",
            driveType: "Mecanum",
            length: 1.5,
            width: 0.9,
            maxSpeed: 1.2,
            homeNodeId: "CH-A"));

        Assert.Equal("AMR-测试", vehicle.Name);
        Assert.Equal(Fms.Domain.Shared.Enums.DriveType.Mecanum, vehicle.Kinematics.DriveType);
        Assert.Equal(1.5, vehicle.Kinematics.Length);
        Assert.Equal(0.9, vehicle.Kinematics.Width);
        Assert.Equal(1.2, vehicle.Kinematics.MaxSpeed);
        Assert.Equal("CH-A", vehicle.HomeNodeId);

        Assert.False(engine.UpdateVehicleLedger("V1", homeNodeId: "L1"));
        Assert.Equal("CH-A", vehicle.HomeNodeId);
    }
}
