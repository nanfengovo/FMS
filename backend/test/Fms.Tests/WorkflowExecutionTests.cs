using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Domain.Workflow;
using Fms.Engine;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fms.Tests;

public class WorkflowExecutionTests
{
    [Fact]
    public void WorkflowOrder_MultiStepExecution_AdvanceAndCompleteSuccessfully()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("STK-01", new Point2D(0, 0), NodeType.JobPoint);
        var nB = new MapNode("WB-01", new Point2D(10, 0), NodeType.JobPoint);
        graph.AddNode(nA);
        graph.AddNode(nB);
        graph.AddPath(new StraightLinePath("P1", "STK-01", "WB-01", nA.Position, nB.Position, bidirectional: true));

        var vehicle = new VehicleAggregate("AMR-101", "AMR-101", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "STK-01"
        };
        engine.RegisterVehicle(vehicle);

        var order = new TransportOrderAggregate("WF-TEST-001", priority: 80, bindVehicleId: "AMR-101")
        {
            TaskType = "Workflow"
        };

        // 构造三步工序: 1. 顶升取料 (0.2s) -> 2. 导航至 WB-01 -> 3. 下降卸料 (0.2s)
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Name = "顶升取料",
            Action = ActionType.LiftUp,
            TargetNodeId = "STK-01",
            DwellSec = 0.2
        });
        order.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            Name = "导航至WB-01",
            Action = ActionType.Move,
            TargetNodeId = "WB-01",
            DwellSec = 0.1
        });
        order.Steps.Add(new OrderStep
        {
            StepIndex = 2,
            Name = "下降卸料",
            Action = ActionType.PutDown,
            TargetNodeId = "WB-01",
            DwellSec = 0.2
        });

        engine.SubmitOrder(order);

        // 步进仿真驱动执行
        for (int i = 0; i < 200; i++)
        {
            engine.AdvanceTick(0.1);
            if (order.IsFinished) break;
        }

        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(3, order.CurrentStepIndex);
        Assert.Null(vehicle.AssignedOrderId);
    }

    [Fact]
    public void WorkflowOrder_SkipStep_AdvancesToNextStepImmediately()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("STK-01", new Point2D(0, 0), NodeType.JobPoint);
        graph.AddNode(nA);

        var vehicle = new VehicleAggregate("AMR-101", "AMR-101", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "STK-01"
        };
        engine.RegisterVehicle(vehicle);

        var order = new TransportOrderAggregate("WF-SKIP-001", priority: 80, bindVehicleId: "AMR-101")
        {
            TaskType = "Workflow"
        };

        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Name = "长时间工艺等待",
            Action = ActionType.Wait,
            TargetNodeId = "STK-01",
            DwellSec = 999.0
        });
        order.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            Name = "第二工步等待",
            Action = ActionType.Wait,
            TargetNodeId = "STK-01",
            DwellSec = 5.0
        });

        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        // 当前停在第一步超长等待
        Assert.Equal(0, order.CurrentStepIndex);

        // 手动跳步入队，需下一拍主循环消费执行
        engine.SkipStep(order.Id);
        engine.AdvanceTick(0.1);

        Assert.Equal(1, order.CurrentStepIndex);
    }

    [Fact]
    public async Task WorkflowTemplate_SqlitePersistence_SaveAndQuery()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Fms.EntityFrameworkCore.FmsDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var db = new Fms.EntityFrameworkCore.FmsDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();

            var record = new Fms.EntityFrameworkCore.Entities.WorkflowTemplateRecord
            {
                Id = "WF-TEST-PERSIST-001",
                Name = "晶圆高精倒运测试流程",
                Description = "自动化测试验证 SQLite 持久化入库",
                IsBuiltIn = false,
                StepsJson = "[{\"id\":\"s1\",\"name\":\"取料\"},{\"id\":\"s2\",\"name\":\"送料\"}]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.WorkflowTemplates.Add(record);
            await db.SaveChangesAsync();
        }

        using (var db = new Fms.EntityFrameworkCore.FmsDbContext(options))
        {
            var queried = await db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == "WF-TEST-PERSIST-001");
            Assert.NotNull(queried);
            Assert.Equal("晶圆高精倒运测试流程", queried.Name);
            Assert.Contains("送料", queried.StepsJson);
            Assert.False(queried.IsBuiltIn);
        }
    }

    [Fact]
    public void WorkflowOrder_DecisionBranch_JumpsAccordingToSocCondition()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("STK-01", new Point2D(0, 0), NodeType.JobPoint);
        var nChg = new MapNode("CH-01", new Point2D(20, 0), NodeType.ChargePoint);
        var nPark = new MapNode("PARK-01", new Point2D(0, 20), NodeType.PullOverPoint);
        graph.AddNode(nA);
        graph.AddNode(nChg);
        graph.AddNode(nPark);
        graph.AddPath(new StraightLinePath("P1", "STK-01", "CH-01", nA.Position, nChg.Position, bidirectional: true));
        graph.AddPath(new StraightLinePath("P2", "STK-01", "PARK-01", nA.Position, nPark.Position, bidirectional: true));

        // 车辆低电量 (SOC = 15.0%)
        var vehicle = new VehicleAggregate("AMR-SOC", "AMR-SOC", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "STK-01"
        };
        vehicle.Bms.SocPercent = 15.0;
        engine.RegisterVehicle(vehicle);

        var order = new TransportOrderAggregate("WF-BRANCH-001", priority: 80, bindVehicleId: "AMR-SOC")
        {
            TaskType = "Workflow"
        };

        // Step 0: 决策分支 (若 SOC < 25%，跳往 STEP-CHG，否则跳往 STEP-PARK)
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            StepId = "STEP-DECIDE",
            Name = "低电量检测决策",
            Action = ActionType.DecisionBranch,
            ConditionType = "SOC_LESS_THAN",
            ConditionValue = "25.0",
            TrueTargetStepId = "STEP-CHG",
            FalseTargetStepId = "STEP-PARK"
        });

        // Step 1: 停靠位
        order.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            StepId = "STEP-PARK",
            Name = "待命停靠",
            Action = ActionType.Wait,
            TargetNodeId = "PARK-01",
            DwellSec = 0.1
        });

        // Step 2: 充电机快充
        order.Steps.Add(new OrderStep
        {
            StepIndex = 2,
            StepId = "STEP-CHG",
            Name = "前往快充",
            Action = ActionType.Wait,
            TargetNodeId = "CH-01",
            DwellSec = 0.1
        });

        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        // 因为电量 15% < 25%，决策成立，应跳转至 Step 2 (STEP-CHG)，跳过 Step 1
        Assert.Equal(2, order.CurrentStepIndex);
        Assert.Equal("STEP-CHG", order.CurrentStep?.StepId);
    }

    [Fact]
    public void WorkflowOrder_ClaimMutex_BlocksWhenOccupied_AdvancesWhenReleased()
    {
        var engine = new DispatchEngine();
        var graph = engine.Graph;

        var nA = new MapNode("ND-A", new Point2D(0, 0), NodeType.JobPoint);
        graph.AddNode(nA);

        var zone = new MutexZone
        {
            Id = "ZONE-TEST-01",
            Name = "测试互斥区",
            MaxCapacity = 1,
            TimeoutSec = 30.0,
            Polygon = new List<Point2D> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }
        };
        graph.AddZone(zone);

        var v1 = new VehicleAggregate("AMR-1", "AMR-1", new Pose2D(0, 0, 0)) { CurrentNodeId = "ND-A" };
        var v2 = new VehicleAggregate("AMR-2", "AMR-2", new Pose2D(0, 0, 0)) { CurrentNodeId = "ND-A" };
        engine.RegisterVehicle(v1);
        engine.RegisterVehicle(v2);

        // v1 先成功占用互斥区
        bool v1Claimed = zone.TryClaim("AMR-1", DateTime.UtcNow);
        Assert.True(v1Claimed);

        // v2 执行工步试图申请同一个互斥区
        var order2 = new TransportOrderAggregate("ORD-MUTEX-WAIT", priority: 80, bindVehicleId: "AMR-2");
        order2.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            Name = "申请互斥区",
            Action = ActionType.ClaimMutex,
            CargoId = "ZONE-TEST-01"
        });
        order2.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            Name = "互斥区内工艺动作",
            Action = ActionType.Wait,
            DwellSec = 0.1
        });

        engine.SubmitOrder(order2);
        engine.AdvanceTick(0.1);

        // 由于 v1 占有着互斥区，v2 的工步应当被阻塞，绝不能提前步进至 Step 1
        Assert.Equal(0, order2.CurrentStepIndex);
        Assert.Equal(OrderStatus.WaitingAction, order2.Status);

        // 现在 v1 释放互斥区
        zone.Release("AMR-1");

        // 经过引擎下一次重试与推进
        engine.AdvanceTick(0.6);

        // v2 成功获得锁并推进至 Step 1
        Assert.Equal(1, order2.CurrentStepIndex);
        Assert.Equal("ZONE-TEST-01", v2.ClaimedMutexZoneId);
    }

    [Fact]
    public void WorkflowOrder_WaitIoSignal_InitialZero_PausesUntilSetOrToggled()
    {
        var engine = new DispatchEngine();
        // 初始状态默认为 "0"
        Assert.Equal("0", engine.GetIoSignal("DOCK_READY"));

        var vehicle = new VehicleAggregate("AMR-IO", "AMR-IO", new Pose2D(0, 0, 0)) { CurrentNodeId = "STK-01" };
        engine.RegisterVehicle(vehicle);

        var order = new TransportOrderAggregate("ORD-IO-WAIT", priority: 80, bindVehicleId: "AMR-IO");
        order.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            StepId = "STEP-01",
            Name = "等待机台DOCK_READY就绪",
            Action = ActionType.WaitIoSignal,
            IoSignalName = "DOCK_READY",
            ExpectedIoValue = "1"
        });
        order.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            StepId = "STEP-02",
            Name = "放行后工艺动作",
            Action = ActionType.Wait,
            DwellSec = 0.1
        });

        engine.SubmitOrder(order);
        engine.AdvanceTick(0.1);

        // 初始 DOCK_READY="0"，车辆应处于 Paused，工单应处于 WaitingAction
        Assert.Equal(0, order.CurrentStepIndex);
        Assert.Equal(OrderStatus.WaitingAction, order.Status);
        Assert.Equal(VehicleStatus.Paused, vehicle.Status);

        // 翻转信号 (0 -> 1)
        engine.SetIoSignal("DOCK_READY", "1");
        Assert.Equal("1", engine.GetIoSignal("DOCK_READY"));

        // 仿真主循环推进 600ms (触发内部重试定时器)
        engine.AdvanceTick(0.6);

        // 验证工单成功通过 STEP-01 并推进至 STEP-02
        Assert.Equal(1, order.CurrentStepIndex);
        Assert.Equal("STEP-02", order.CurrentStep?.StepId);
    }

    [Fact]
    public void WorkflowOrder_DecisionBranch_WithTerminalExit_DoesNotFallThrough()
    {
        var engine = new DispatchEngine();

        // 验证电量充足 (80%) 走 FalseTargetStepId (STEP-PARK)，且因 IsTerminal=true 不会穿透至 STEP-CHG
        var vHigh = new VehicleAggregate("AMR-HIGH", "AMR-HIGH", new Pose2D(0, 0, 0))
        {
            CurrentNodeId = "PARK-01"
        };
        vHigh.Bms.SocPercent = 80.0;
        engine.RegisterVehicle(vHigh);

        var orderHigh = new TransportOrderAggregate("ORD-BRANCH-HIGH", priority: 80, bindVehicleId: "AMR-HIGH");
        orderHigh.Steps.Add(new OrderStep
        {
            StepIndex = 0,
            StepId = "STEP-DECIDE",
            Name = "电量决策",
            Action = ActionType.DecisionBranch,
            ConditionType = "SOC_LESS_THAN",
            ConditionValue = "25.0",
            TrueTargetStepId = "STEP-CHG",
            FalseTargetStepId = "STEP-PARK"
        });
        orderHigh.Steps.Add(new OrderStep
        {
            StepIndex = 1,
            StepId = "STEP-CHG",
            Name = "快充分支",
            Action = ActionType.Wait,
            DwellSec = 0.1,
            IsTerminal = true
        });
        orderHigh.Steps.Add(new OrderStep
        {
            StepIndex = 2,
            StepId = "STEP-PARK",
            Name = "待命分支",
            Action = ActionType.Wait,
            DwellSec = 0.1,
            IsTerminal = true
        });

        engine.SubmitOrder(orderHigh);
        engine.AdvanceTick(0.1);

        // 应该跳转至 STEP-PARK (Index 2)
        Assert.Equal(2, orderHigh.CurrentStepIndex);
        Assert.Equal("STEP-PARK", orderHigh.CurrentStep?.StepId);

        // 执行完 STEP-PARK 后，由于 IsTerminal=true，订单应当直接 Done，绝不落入其他步骤
        engine.AdvanceTick(0.2);
        Assert.Equal(OrderStatus.Done, orderHigh.Status);
    }
}
