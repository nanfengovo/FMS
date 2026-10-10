using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class DeadlockAvoidanceSimulationTests
{
    [Fact]
    public void TwoVehicles_HeadOnConflict_PullOverAvoidanceResolvesDeadlock_BothComplete_NoCollision()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        // 拓扑：A(左:0,0) <==主干道(双向)==> C(中央:15,0) <==主干道(双向)==> B(右:30,0)
        // 中央 C 点连接一个避让侧线 SPUR (15, 8)
        var nA = new MapNode("A", new Point2D(0, 0), NodeType.JobPoint);
        var nB = new MapNode("B", new Point2D(30, 0), NodeType.JobPoint);
        var nC = new MapNode("C", new Point2D(15, 0), NodeType.Landmark);
        var nSpur = new MapNode("SPUR", new Point2D(15, 8), NodeType.PullOverPoint, "避让站");

        graph.AddNode(nA); graph.AddNode(nB); graph.AddNode(nC); graph.AddNode(nSpur);

        graph.AddPath(new StraightLinePath("P_AC", "A", "C", nA.Position, nC.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P_CB", "C", "B", nC.Position, nB.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P_SPUR", "C", "SPUR", nC.Position, nSpur.Position, bidirectional: true));

        // 车 1 (高优先级: 80) 从 A 去 B
        var v1 = new VehicleAggregate("V1", "AMR-1", new Pose2D(0, 0, 0)) { CurrentNodeId = "A" };
        // 车 2 (低优先级: 40) 从 B 去 A (相向对冲)
        var v2 = new VehicleAggregate("V2", "AMR-2", new Pose2D(30, 0, Math.PI)) { CurrentNodeId = "B" };

        engine.RegisterVehicle(v1);
        engine.RegisterVehicle(v2);

        var o1 = new TransportOrderAggregate("TASK-HIGH", priority: 80, bindVehicleId: "V1");
        o1.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = "B" });

        var o2 = new TransportOrderAggregate("TASK-LOW", priority: 40, bindVehicleId: "V2");
        o2.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = "A" });

        engine.SubmitOrder(o1);
        engine.SubmitOrder(o2);

        int conflictEvents = 0;
        engine.ConflictResolved += (_, _) => conflictEvents++;

        double minDistance = double.MaxValue;

        bool v2WasAtSpurWhileV1Passed = false;

        // 步进仿真 60 秒 (600 ticks @ 100ms)
        for (int i = 0; i < 600; i++)
        {
            engine.AdvanceTick(0.1);

            double d = v1.Pose.DistanceTo(v2.Pose);
            if (d < minDistance)
            {
                minDistance = d;
            }

            // 验证关键行为：让行车到达避让站驻留，等优势车通过对冲走廊
            if (v2.IsYielding && (v2.CurrentNodeId == "SPUR" || v2.Pose.DistanceTo(new Pose2D(nSpur.Position.X, nSpur.Position.Y, 0)) < 1.5) && (v1.CurrentNodeId == "C" || v1.CurrentPathId == "P_CB"))
            {
                v2WasAtSpurWhileV1Passed = true;
            }

            if (o1.Status == OrderStatus.Done && o2.Status == OrderStatus.Done)
            {
                break;
            }
        }

        // 1. 断言：高优先级任务成功完成
        Assert.Equal(OrderStatus.Done, o1.Status);
        // 2. 断言：低优先级让行任务在主干道出清后也成功恢复并最终完成！
        Assert.Equal(OrderStatus.Done, o2.Status);

        // 3. 断言：让行车确实在避让站驻留等待优势车通过走廊
        Assert.True(v2WasAtSpurWhileV1Passed, "让行车必须在避让站SPUR安全驻留等待，直到优势车通过冲突走廊");

        // 4. 断言：全程最小两车间距大于安全包络阈值 (无碰撞发生，最小间距大于2.0米)
        Assert.True(minDistance > 2.0, $"两车最小物理间距必须安全大于2.0米，实际测得: {minDistance:F2}m");

        // 4. 断言：避让决策具有确定性，冲突仲裁次数有严格上界 (不会无休止震荡重规划)
        Assert.InRange(conflictEvents, 1, 4);
    }

    [Fact]
    public void TwoVehicles_SameDirection_CarFollowing_ShouldNotTriggerDeadlockAvoidance()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("A", new Point2D(0, 0), NodeType.JobPoint);
        var nMid = new MapNode("MID", new Point2D(20, 0), NodeType.Landmark);
        var nB = new MapNode("B", new Point2D(50, 0), NodeType.JobPoint);
        graph.AddNode(nA); graph.AddNode(nMid); graph.AddNode(nB);
        graph.AddPath(new StraightLinePath("P_A_MID", "A", "MID", nA.Position, nMid.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P_MID_B", "MID", "B", nMid.Position, nB.Position, bidirectional: true));

        // 前车 V1 从 MID(20,0) 出发去 B
        var v1 = new VehicleAggregate("V1", "AMR-LEADER", new Pose2D(20, 0, 0)) { CurrentNodeId = "MID" };
        // 后车 V2 从 A(0,0) 出发同向去 B
        var v2 = new VehicleAggregate("V2", "AMR-FOLLOWER", new Pose2D(0, 0, 0)) { CurrentNodeId = "A" };

        engine.RegisterVehicle(v1);
        engine.RegisterVehicle(v2);

        var o1 = new TransportOrderAggregate("TASK-LEAD", priority: 50, bindVehicleId: "V1");
        o1.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = "B" });

        var o2 = new TransportOrderAggregate("TASK-FOLLOW", priority: 50, bindVehicleId: "V2");
        o2.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = "MID" });

        engine.SubmitOrder(o1);
        engine.SubmitOrder(o2);

        int falseDeadlockEvents = 0;
        engine.ConflictResolved += (_, _) => falseDeadlockEvents++;

        double minDistance = double.MaxValue;

        for (int i = 0; i < 600; i++)
        {
            engine.AdvanceTick(0.1);
            double d = v1.Pose.DistanceTo(v2.Pose);
            if (d < minDistance) minDistance = d;

            if (o1.Status == OrderStatus.Done && o2.Status == OrderStatus.Done)
                break;
        }

        // 断言：同向跟车两车都完成
        Assert.Equal(OrderStatus.Done, o1.Status);
        Assert.Equal(OrderStatus.Done, o2.Status);

        // 断言：同向跟车绝不误报对向死锁冲突避让！
        Assert.Equal(0, falseDeadlockEvents);

        // 断言：安全跟车间距始终保持
        Assert.True(minDistance >= 1.0, $"跟车间距安全，最小间距: {minDistance:F2}m");
    }

    [Fact]
    public void BatteryAutonomousRecharge_Reaches85Percent_ThenCompletesOrderAndBecomesIdle()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nStart = new MapNode("P1", new Point2D(0, 0), NodeType.JobPoint);
        var nCharge = new MapNode("CH-01", new Point2D(10, 0), NodeType.ChargePoint, "快充桩");
        graph.AddNode(nStart);
        graph.AddNode(nCharge);
        graph.AddPath(new StraightLinePath("P_START_CH", "P1", "CH-01", nStart.Position, nCharge.Position, bidirectional: true));

        var v = new VehicleAggregate("V-LOW", "AMR-BAT", new Pose2D(0, 0, 0)) { CurrentNodeId = "P1" };
        v.Bms.SocPercent = 18.0; // 低电量
        engine.RegisterVehicle(v);

        bool orderFinished = false;
        engine.OrderCompleted += (_, order) =>
        {
            if (order.TaskType == "Charge")
            {
                orderFinished = true;
            }
        };

        // 步进仿真：车辆应自动巡检到低电量 -> 自动创建回充单 -> 驶向 CH-01 -> 充电至 >= 85% -> 订单完成 -> 车辆恢复 Idle
        for (int i = 0; i < 2000; i++)
        {
            engine.AdvanceTick(0.1);
            if (orderFinished) break;
        }

        Assert.True(orderFinished, "自动回充工单必须在达到 85% SOC 后完成");
        Assert.True(v.Bms.SocPercent >= 85.0, $"充能后 SOC 必须达到 >= 85.0%，实际为: {v.Bms.SocPercent:F1}%");
        Assert.False(v.Bms.IsCharging, "充电完成后 IsCharging 必须重置为 false");
        Assert.Equal(VehicleStatus.Idle, v.Status);
    }

    [Fact]
    public void MutexZone_AdmissionDenied_HaltsApproachingVehicleWithoutIntrusion()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("A", new Point2D(0, 5), NodeType.JobPoint);
        var nIn = new MapNode("ZONE_IN", new Point2D(10, 5), NodeType.Landmark);
        var nOut = new MapNode("ZONE_OUT", new Point2D(20, 5), NodeType.Landmark);
        var nB = new MapNode("B", new Point2D(30, 5), NodeType.JobPoint);

        graph.AddNode(nA); graph.AddNode(nIn); graph.AddNode(nOut); graph.AddNode(nB);
        graph.AddPath(new StraightLinePath("P_A_IN", "A", "ZONE_IN", nA.Position, nIn.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P_ZONE", "ZONE_IN", "ZONE_OUT", nIn.Position, nOut.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P_OUT_B", "ZONE_OUT", "B", nOut.Position, nB.Position, bidirectional: true));

        var zone = new MutexZone
        {
            Id = "ZONE-MUTEX-TEST",
            Name = "测试互斥区",
            MaxCapacity = 1,
            TimeoutSec = 60.0,
            Polygon = new List<Point2D>
            {
                new(9.0, 0.0),
                new(21.0, 0.0),
                new(21.0, 10.0),
                new(9.0, 10.0)
            },
            AssociatedNodeIds = new HashSet<string> { "ZONE_IN", "ZONE_OUT" },
            AssociatedPathIds = new HashSet<string> { "P_ZONE" }
        };
        graph.AddZone(zone);

        // V1 已经占有互斥区
        zone.TryClaim("V1", engine.Clock.CurrentTimeUtc);

        // V2 从 A 出发试图穿过互斥区去 B
        var v2 = new VehicleAggregate("V2", "AMR-TEST-2", new Pose2D(0, 5, 0)) { CurrentNodeId = "A" };
        engine.RegisterVehicle(v2);

        var order2 = new TransportOrderAggregate("TASK-V2", priority: 50, bindVehicleId: "V2");
        order2.Steps.Add(new OrderStep { StepIndex = 0, Action = ActionType.Move, TargetNodeId = "B" });
        engine.SubmitOrder(order2);

        // 步进仿真 100 拍 (10 秒)
        for (int i = 0; i < 100; i++)
        {
            engine.AdvanceTick(0.1);
        }

        // V2 应该在接近互斥区边缘被拦住，速度为 0，且车身中心决不能进入互斥区多边形内部
        Assert.Equal("V1", v2.BlockedByVehicleId);
        Assert.Equal(0.0, v2.CurrentSpeed);
        Assert.False(zone.ContainsPoint(v2.Pose.Position), "互斥区申请失败时，车辆严禁闯入多边形内部");
    }
}

