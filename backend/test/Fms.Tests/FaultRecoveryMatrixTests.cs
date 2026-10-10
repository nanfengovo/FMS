using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class FaultRecoveryMatrixTests
{
    [Fact]
    public void Vehicle_HeartbeatTimeout_TriggersOfflineAndSafetyPause_AndRecoversOnHeartbeat()
    {
        var engine = new DispatchEngine();
        var v = new VehicleAggregate("AGV-HB", "AGV-HB", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "STK-01",
            Status = VehicleStatus.Running,
            LastHeartbeatUtc = engine.Clock.CurrentTimeUtc
        };
        engine.RegisterVehicle(v);

        var order = new TransportOrderAggregate("ORD-HB-TEST", priority: 80, bindVehicleId: "AGV-HB");
        order.Steps.Add(new OrderStep { StepIndex = 0, Name = "运行中步骤", Action = ActionType.Wait, DwellSec = 10.0 });
        engine.SubmitOrder(order);

        engine.AdvanceTick(0.1);
        Assert.True(v.IsOnline);

        // 模拟时间流逝 6 秒 (心跳超时阈值为 5.0 秒)
        v.LastHeartbeatUtc = engine.Clock.CurrentTimeUtc.AddSeconds(-6.0);
        engine.AdvanceTick(0.1);

        // 验证车辆被判定掉线并停止物理位移
        Assert.False(v.IsOnline);
        Assert.Equal(0.0, v.CurrentSpeed);
        Assert.Equal(VehicleStatus.Fault, v.Status);
        Assert.Equal(OrderStatus.WaitingAction, order.Status);
        Assert.Contains("通信掉线中断", order.FailReason ?? "");

        // 模拟车体恢复通信上报心跳
        engine.RecordVehicleHeartbeat(v.Id);
        Assert.True(v.IsOnline);
        Assert.Equal(VehicleStatus.Idle, v.Status);
    }

    [Fact]
    public void DynamicClosure_ReroutesRunningVehicleAroundBlockedPath()
    {
        var engine = new DispatchEngine();
        // 构造环形可选路线图: N0 -> N1 -> N3 (主航道); N0 -> N2 -> N3 (旁路避障航道)
        engine.Graph.AddNode(new MapNode("N0", new Point2D(0, 0)));
        engine.Graph.AddNode(new MapNode("N1", new Point2D(10, 0)));
        engine.Graph.AddNode(new MapNode("N2", new Point2D(0, 10)));
        engine.Graph.AddNode(new MapNode("N3", new Point2D(10, 10)));

        engine.Graph.AddPath(new StraightLinePath("P-0-1", "N0", "N1", new Point2D(0, 0), new Point2D(10, 0)));
        engine.Graph.AddPath(new StraightLinePath("P-1-3", "N1", "N3", new Point2D(10, 0), new Point2D(10, 10)));
        engine.Graph.AddPath(new StraightLinePath("P-0-2", "N0", "N2", new Point2D(0, 0), new Point2D(0, 10)));
        engine.Graph.AddPath(new StraightLinePath("P-2-3", "N2", "N3", new Point2D(0, 10), new Point2D(10, 10)));

        var v = new VehicleAggregate("AGV-REROUTE", "AGV-REROUTE", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "N0",
            TargetNodeId = "N3",
            Status = VehicleStatus.Running,
            RoutePathIds = new List<string> { "P-0-1", "P-1-3" }
        };
        engine.RegisterVehicle(v);

        // 临时封锁主航道 P-0-1
        bool closed = engine.SetPathClosed("P-0-1", true, "施工维护临时封闭");
        Assert.True(closed);

        // 验证车辆自动动态重路由至旁路 N2 -> N3
        Assert.Contains("P-0-2", v.RoutePathIds);
        Assert.Contains("P-2-3", v.RoutePathIds);
        Assert.DoesNotContain("P-0-1", v.RoutePathIds);
        Assert.Equal(AvoidanceStrategy.DynamicReroute, v.CurrentAvoidanceStrategy);
    }

    [Fact]
    public void StepFailure_RetryAndRollbackOrder_FunctionCorrectly()
    {
        var engine = new DispatchEngine();
        engine.Graph.AddNode(new MapNode("STK-01", new Point2D(0, 0)));
        engine.Graph.AddNode(new MapNode("PARK-01", new Point2D(20, 20)));
        engine.Graph.AddPath(new StraightLinePath("P-RETREAT", "STK-01", "PARK-01", new Point2D(0, 0), new Point2D(20, 20)));

        var v = new VehicleAggregate("AGV-RETRY", "AGV-RETRY", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "STK-01",
            Status = VehicleStatus.Idle
        };
        engine.RegisterVehicle(v);

        var order = new TransportOrderAggregate("ORD-FAIL-TEST", priority: 80, bindVehicleId: "AGV-RETRY");
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Name = "工艺对接",
            Action = ActionType.Wait,
            DwellSec = 5.0
        });
        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        // 触发重试
        bool retried = engine.RetryStep(order.Id);
        Assert.True(retried);
        Assert.Equal(OrderStatus.Running, order.Status);
        Assert.Null(order.FailReason);

        // 触发安全回退撤退至待命区 PARK-01
        bool rollback = engine.SafeRollbackOrder(order.Id, "PARK-01");
        Assert.True(rollback);
        engine.AdvanceTick(0.1);
        Assert.Equal(OrderStatus.WaitingAction, order.Status);
        Assert.Equal("PARK-01", v.TargetNodeId);
        Assert.Contains("安全回退", order.FailReason ?? "");
    }
}
