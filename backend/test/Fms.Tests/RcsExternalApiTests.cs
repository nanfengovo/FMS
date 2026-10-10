using Fms.Application;
using Fms.Application.Map;
using Fms.Application.Rcs;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class RcsExternalApiTests
{
    [Fact]
    public void ConfigurationService_Should_Parse_CustomSet_And_KcFmsXml()
    {
        var config = new RcsConfigurationService();
        
        // Assert defaults or loaded values
        Assert.NotNull(config.CustomIp);
        Assert.True(config.CustomPort > 0);
        Assert.NotNull(config.PushServerIp);
        Assert.True(config.PushServerPort > 0);
        
        // Verify URL mapping with underscore type prefix
        string urlWithPrefix = config.GetEndpointUrl("task_info", "PA_14_get");
        Assert.Contains("/PA/api/v1/xinsong/task_info", urlWithPrefix);

        string urlWithoutPrefix = config.GetEndpointUrl("task_info", "MOVE");
        Assert.Contains("/api/v1/xinsong/task_info", urlWithoutPrefix);
        Assert.DoesNotContain("/MOVE/", urlWithoutPrefix);
    }

    [Fact]
    public void DispatchEngine_Should_Trigger_Rcs_Task_Lifecycle_Events()
    {
        var engine = new DispatchEngine();
        
        // Setup minimal graph
        var n1 = new MapNode("N1", new Point2D(0, 0), NodeType.JobPoint);
        var n2 = new MapNode("N2", new Point2D(10, 0), NodeType.JobPoint);
        engine.Graph.AddNode(n1);
        engine.Graph.AddNode(n2);
        engine.Graph.AddPath(new StraightLinePath("P1", "N1", "N2", n1.Position, n2.Position, bidirectional: true));

        // Vehicle
        var amr = new VehicleAggregate("AGV-1", "AGV-1", new Pose2D(0, 0, 0)) { CurrentNodeId = "N1" };
        engine.RegisterVehicle(amr);

        bool startedCalled = false;
        bool arrivedCalled = false;
        bool completedCalled = false;

        engine.OrderStarted += (_, e) =>
        {
            startedCalled = true;
            Assert.Equal("TASK-001", e.Order.OrderSerial);
            Assert.Equal("AGV-1", e.Vehicle.Id);
        };

        engine.OrderArrivedTarget += (_, e) =>
        {
            arrivedCalled = true;
            Assert.Equal("N2", e.TargetNodeId);
        };

        engine.OrderCompleted += (_, order) =>
        {
            completedCalled = true;
            Assert.Equal("TASK-001", order.OrderSerial);
        };

        // Submit task with sub_task properties
        var order = new TransportOrderAggregate("TASK-001", 50, "AGV-1")
        {
            TaskType = "PA_test"
        };
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Action = ActionType.Move,
            TargetNodeId = "N2",
            GoalAction = 1,
            OptionCode = "0,0",
            CargoId = "CARGO-123"
        });

        engine.SubmitOrder(order);

        // Advance simulation ticks
        for (int i = 0; i < 200; i++)
        {
            engine.AdvanceTick(0.1);
        }

        Assert.True(startedCalled, "OrderStarted event should have been fired");
        Assert.True(arrivedCalled, "OrderArrivedTarget event should have been fired");
        Assert.True(completedCalled, "OrderCompleted event should have been fired");
    }

    [Fact]
    public void TaskFinished_Payload_Should_Track_WalkTime_And_CargoActionTime()
    {
        var order = new TransportOrderAggregate("TASK-FINISH-TEST", 50)
        {
            TotalWalkTimeCostSec = 12.5,
            TotalCargoActionTimeCostSec = 5.0,
            TotalWaitingTimeCostSec = 2.0,
            TotalOdomMeter = 18.0
        };

        Assert.Equal(12.5, order.TotalWalkTimeCostSec);
        Assert.Equal(5.0, order.TotalCargoActionTimeCostSec);
        Assert.Equal(2.0, order.TotalWaitingTimeCostSec);
        Assert.Equal(18.0, order.TotalOdomMeter);
    }
}
