using Fms.Application.Logging;
using Fms.Domain.Logging;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Storage;
using Fms.Domain.Topology;
using Fms.Domain.Traffic;
using Fms.Domain.Vehicles;
using Fms.Domain.Workflow;
using Xunit;

namespace Fms.Tests;

public class DeepSchedulingArchitectureTests
{
    [Fact]
    public void ReservationManager_OpposingCorridorConflict_RearVehicleDeniedAndYields()
    {
        var mgr = new ResourceReservationManager();
        var graph = new TopologyGraph();
        var nA = new MapNode("NODE_A", new Point2D(0, 0));
        var nB = new MapNode("NODE_B", new Point2D(10, 0));
        graph.AddNode(nA);
        graph.AddNode(nB);
        graph.AddPath(new StraightLinePath("PATH_CORRIDOR_1", "NODE_A", "NODE_B", nA.Position, nB.Position, bidirectional: true));

        var v1 = new VehicleAggregate("VEH-01", "AGV-01", new Pose2D(0, 0, 0));
        var seg1 = new ItinerarySegment
        {
            SegmentIndex = 0,
            PathId = "PATH_CORRIDOR_1",
            FromNodeId = "NODE_A",
            ToNodeId = "NODE_B",
            TrajectoryPoints = new List<Point2D> { new(0, 0), new(10, 0) },
            MotionMode = SegmentMotionMode.Forward,
            EstimatedDurationSec = 8.0
        };

        // 车 1 成功预约 A -> B
        var result1 = mgr.TryClaimSegment(v1, seg1, DateTime.UtcNow, remainingDist: 10.0, order: null, graph);
        Assert.True(result1.Success);

        // 车 2 尝试反向进入 B -> A (对向走廊冲突)
        var v2 = new VehicleAggregate("VEH-02", "AGV-02", new Pose2D(10, 0, Math.PI));
        var seg2 = new ItinerarySegment
        {
            SegmentIndex = 0,
            PathId = "PATH_CORRIDOR_1",
            FromNodeId = "NODE_B",
            ToNodeId = "NODE_A",
            TrajectoryPoints = new List<Point2D> { new(10, 0), new(0, 0) },
            MotionMode = SegmentMotionMode.Forward, // 拓扑反向
            EstimatedDurationSec = 8.0
        };

        var result2 = mgr.TryClaimSegment(v2, seg2, DateTime.UtcNow, remainingDist: 10.0, order: null, graph);
        Assert.False(result2.Success);
        Assert.True(result2.ShouldYieldOrWait);
        Assert.Contains("对向航道", result2.Reason);
        Assert.Equal("VEH-01", result2.BlockedByVehicleId);
    }

    [Fact]
    public void ReservationManager_CarFollowing_AllowsSameDirectionSharing()
    {
        var mgr = new ResourceReservationManager();
        var graph = new TopologyGraph();
        var nA = new MapNode("N1", new Point2D(0, 0));
        var nB = new MapNode("N2", new Point2D(20, 0));
        graph.AddNode(nA);
        graph.AddNode(nB);
        graph.AddPath(new StraightLinePath("PATH_MAIN", "N1", "N2", nA.Position, nB.Position, bidirectional: true));

        var v1 = new VehicleAggregate("VEH-01", "Leader", new Pose2D(0, 0, 0));
        var seg1 = new ItinerarySegment
        {
            SegmentIndex = 0,
            PathId = "PATH_MAIN",
            FromNodeId = "N1",
            ToNodeId = "N2",
            TrajectoryPoints = new List<Point2D> { new(0, 0), new(20, 0) },
            MotionMode = SegmentMotionMode.Forward,
            EstimatedDurationSec = 15.0
        };

        var result1 = mgr.TryClaimSegment(v1, seg1, DateTime.UtcNow, remainingDist: 20.0, order: null, graph);
        Assert.True(result1.Success);

        // 车 2 同向跟驰：同向 N1 -> N2
        var v2 = new VehicleAggregate("VEH-02", "Follower", new Pose2D(0, 0, 0));
        var seg2 = new ItinerarySegment
        {
            SegmentIndex = 0,
            PathId = "PATH_MAIN",
            FromNodeId = "N1",
            ToNodeId = "N2",
            TrajectoryPoints = new List<Point2D> { new(0, 0), new(20, 0) },
            MotionMode = SegmentMotionMode.Forward,
            EstimatedDurationSec = 15.0
        };

        var result2 = mgr.TryClaimSegment(v2, seg2, DateTime.UtcNow, remainingDist: 20.0, order: null, graph);
        Assert.True(result2.Success);
        Assert.True(result2.IsCarFollowing);
        Assert.Equal("VEH-01", result2.BlockedByVehicleId);
    }

    [Fact]
    public void StorageManager_FullStateMachine_DropPickLockUnlock()
    {
        var sm = new StorageManager();
        var slot = sm.GetSlot("B-02");
        Assert.NotNull(slot);
        Assert.Equal(StorageSlotStatus.Empty, slot.Status);

        // 1. 预约放料
        var (okDropRes, _) = slot.ReserveForDrop("ORD-100", "VEH-01");
        Assert.True(okDropRes);
        Assert.Equal(StorageSlotStatus.Reserved, slot.Status);
        Assert.Equal("ORD-100", slot.ReservedOrderId);

        // 2. 确认放料
        var (okDropConfirm, _) = slot.ConfirmDrop("FOUP-999");
        Assert.True(okDropConfirm);
        Assert.Equal(StorageSlotStatus.Occupied, slot.Status);
        Assert.Equal("FOUP-999", slot.OccupiedCarrierId);

        // 3. 预约取料
        var (okPickRes, _) = slot.ReserveForPick("ORD-101", "VEH-02", "FOUP-999");
        Assert.True(okPickRes);
        Assert.Equal(StorageSlotStatus.Reserved, slot.Status);

        // 4. 确认取料
        var (okPickConfirm, _) = slot.ConfirmPick();
        Assert.True(okPickConfirm);
        Assert.Equal(StorageSlotStatus.Empty, slot.Status);
        Assert.Null(slot.OccupiedCarrierId);

        // 5. 锁定与解锁
        slot.Lock("光电传感器检修");
        Assert.Equal(StorageSlotStatus.Locked, slot.Status);
        Assert.Equal("光电传感器检修", slot.LockReason);

        // 锁定态无法预约
        var (failDrop, _) = slot.ReserveForDrop("ORD-102", "VEH-03");
        Assert.False(failDrop);

        slot.Unlock();
        Assert.Equal(StorageSlotStatus.Empty, slot.Status);
        Assert.Null(slot.LockReason);
    }

    [Fact]
    public void StorageManager_ZonePolicies_EnforcesRawAndFinishedRules()
    {
        var sm = new StorageManager();

        // 原料区只出不进：尝试为放料分配原料区货位应当被拒绝
        var (rawDropOk, rawReason, _) = sm.AllocateSlotForDrop(StorageZoneType.Raw, "ORD-RAW", "VEH-01");
        Assert.False(rawDropOk);
        Assert.Contains("只出不进", rawReason);

        // WIP 缓存区支持入库放料
        var (wipDropOk, _, wipSlot) = sm.AllocateSlotForDrop(StorageZoneType.Wip, "ORD-WIP", "VEH-01");
        Assert.True(wipDropOk);
        Assert.NotNull(wipSlot);
        Assert.Equal(StorageSlotStatus.Reserved, wipSlot.Status);

        // 成品区只进不出：尝试取料出库应当被拒绝
        var finSlot = sm.GetSlot("C-02");
        Assert.NotNull(finSlot);
        var (finPickOk, finReason) = sm.ExecutePick(finSlot.Id);
        Assert.False(finPickOk);
        Assert.Contains("只进不出", finReason);
    }

    [Fact]
    public void RoadNetworkManager_PublishSafetyInterlock_BlocksWhenVehiclesRunning()
    {
        var rnm = new RoadNetworkManager();
        var sm = new StorageManager();

        var v1 = new VehicleAggregate("V1", "AGV-01", new Pose2D(0, 0, 0))
        {
            IsOnline = true,
            Status = VehicleStatus.Running
        };
        v1.RoutePathIds.Add("P1"); // 在途有航道

        // 尝试发布
        var result = rnm.Publish("Dispatcher", sm, new[] { v1 });
        Assert.False(result.Success);
        Assert.Contains("安全互锁阻断", result.Message);
        Assert.Contains("V1", result.Message);

        // 车停下出清航道后允许发布
        v1.Status = VehicleStatus.Idle;
        v1.RoutePathIds.Clear();

        var resultAfterStop = rnm.Publish("Dispatcher", sm, new[] { v1 });
        Assert.True(resultAfterStop.Success);
        Assert.NotNull(resultAfterStop.Version);
        Assert.NotNull(rnm.ActiveRoadNetwork);
        Assert.Equal(resultAfterStop.Version, rnm.ActiveRoadNetwork.Version);
    }

    [Fact]
    public void WorkflowExecutionManager_BranchingAndStateTransitions()
    {
        var wm = new WorkflowExecutionManager();
        var def = new WorkflowDefinition
        {
            Id = "WF_TEST_BRANCH",
            Name = "条件分支自动化流程",
            Version = 1,
            Nodes = new List<WorkflowNodeDefinition>
            {
                new()
                {
                    Id = "N_CHECK",
                    ActionType = "DECISION_BRANCH",
                    ConditionType = "SOC_LESS_THAN",
                    ConditionValue = "30",
                    TrueTargetNodeId = "N_CHARGE",
                    FalseTargetNodeId = "N_PARK"
                },
                new()
                {
                    Id = "N_CHARGE",
                    ActionType = "CHARGE",
                    TargetNodeId = "CH-01",
                    IsTerminal = true
                },
                new()
                {
                    Id = "N_PARK",
                    ActionType = "MOVE_TO_POINT",
                    TargetNodeId = "PARK-01",
                    IsTerminal = true
                }
            }
        };

        wm.RegisterDefinition(def);

        // 1. 低电量车 (SOC = 20%) -> 应当分支流转到 N_CHARGE
        var lowBatVehicle = new VehicleAggregate("V_LOW", "LowBatAGV", new Pose2D(0, 0, 0));
        lowBatVehicle.Bms.SocPercent = 20.0;

        var inst1 = wm.StartInstance("WF_TEST_BRANCH", "V_LOW");
        Assert.NotNull(inst1);

        var orders1 = new List<TransportOrderAggregate>();
        wm.AdvanceInstances(
            sig => "0",
            id => id == "V_LOW" ? lowBatVehicle : null,
            id => null,
            order => orders1.Add(order)
        );

        Assert.Equal("N_CHARGE", inst1.CurrentNodeId);
        Assert.Single(orders1);
        Assert.Equal("CH-01", orders1[0].Steps[0].TargetNodeId);

        // 2. 充足电量车 (SOC = 80%) -> 应当分支流转到 N_PARK
        var highBatVehicle = new VehicleAggregate("V_HIGH", "HighBatAGV", new Pose2D(0, 0, 0));
        highBatVehicle.Bms.SocPercent = 80.0;

        var inst2 = wm.StartInstance("WF_TEST_BRANCH", "V_HIGH");
        Assert.NotNull(inst2);

        var orders2 = new List<TransportOrderAggregate>();
        wm.AdvanceInstances(
            sig => "0",
            id => id == "V_HIGH" ? highBatVehicle : null,
            id => null,
            order => orders2.Add(order)
        );

        Assert.Equal("N_PARK", inst2.CurrentNodeId);
        Assert.Single(orders2);
        Assert.Equal("PARK-01", orders2[0].Steps[0].TargetNodeId);
    }

    [Fact]
    public void DecisionLogService_RecordsAndQueriesAccurately()
    {
        var logger = new DecisionLogService(maxCapacity: 100);

        logger.Record(DecisionCategory.Routing, "ORD-001", "PLAN_ITINERARY", "SUCCESS", "找到全局最短时间行程", new { Cost = 12.5 });
        logger.Record(DecisionCategory.TrafficAvoidance, "VEH-002", "YIELD_WAIT", "BLOCKED", "对向走廊已被高优先级工单占用", new { ConflictPath = "P-MAIN-1" });
        logger.Record(DecisionCategory.ResourceLease, "SLOT-A-01", "ALLOCATE_SLOT", "SUCCESS", "成功分配原料库位 A-01");

        var (all, count) = logger.Query();
        Assert.Equal(3, count);
        Assert.Equal(3, all.Count);

        // 按类型查询
        var (yielding, yCount) = logger.Query(category: DecisionCategory.TrafficAvoidance);
        Assert.Equal(1, yCount);
        Assert.Equal("VEH-002", yielding[0].SubjectId);
        Assert.Contains("对向走廊", yielding[0].Reason);

        // 按主题 ID 查询
        var (sub, sCount) = logger.Query(subjectId: "ORD-001");
        Assert.Equal(1, sCount);
        Assert.Equal(DecisionCategory.Routing, sub[0].Category);
    }

    [Fact]
    public void NavigationTracker_EvaluatesDeviationAndArrivalTolerance()
    {
        var tracker = new NavigationTracker
        {
            LateralToleranceMeters = 0.8,
            ArrivalDistanceToleranceMeters = 0.15,
            ArrivalHeadingToleranceRad = 0.15
        };

        var segment = new ItinerarySegment
        {
            SegmentIndex = 0,
            PathId = "P-NAV-1",
            FromNodeId = "N1",
            ToNodeId = "N2",
            TrajectoryPoints = new List<Point2D> { new(0, 0), new(5, 0), new(10, 0) },
            MotionMode = SegmentMotionMode.Forward
        };

        // 1. 沿线正常巡航 (横向偏移 0.1m < 0.8m)
        var vNormal = new VehicleAggregate("V-NAV-1", "AMR-1", new Pose2D(3.0, 0.1, 0.0));
        var repNormal = tracker.EvaluateTracking(vNormal, segment, new Point2D(10, 0), 0.0);
        Assert.False(repNormal.RequiresReroute);
        Assert.False(repNormal.IsWithinArrivalTolerance);
        Assert.True(repNormal.LateralDeviationMeters <= 0.15);

        // 2. 偏差超限 (横向偏移 1.2m > 0.8m)
        var vOffTrack = new VehicleAggregate("V-NAV-2", "AMR-2", new Pose2D(3.0, 1.2, 0.0));
        var repOffTrack = tracker.EvaluateTracking(vOffTrack, segment, new Point2D(10, 0), 0.0);
        Assert.True(repOffTrack.RequiresReroute);
        Assert.Contains("重规划", repOffTrack.Message);

        // 3. 精准到达终点 (距离 0.05m，航向偏差 0.02 rad)
        var vArrived = new VehicleAggregate("V-NAV-3", "AMR-3", new Pose2D(10.02, 0.01, 0.02));
        var repArrived = tracker.EvaluateTracking(vArrived, segment, new Point2D(10, 0), 0.0);
        Assert.True(repArrived.IsWithinArrivalTolerance);
        Assert.False(repArrived.RequiresReroute);
    }

    [Fact]
    public void IdentityService_ActionBasedPermission_And_ForceReleaseSafetyInterlock()
    {
        var identity = new Fms.Application.Identity.IdentityService();
        var engine = new Fms.Engine.DispatchEngine();

        // 1. 细粒度动作授权判定
        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Admin", Fms.Domain.Identity.FmsPermissions.TrafficForceRelease));
        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Engineer", Fms.Domain.Identity.FmsPermissions.TrafficForceRelease));
        Assert.False(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Operator", Fms.Domain.Identity.FmsPermissions.TrafficForceRelease));
        Assert.False(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Viewer", Fms.Domain.Identity.FmsPermissions.TrafficForceRelease));

        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Admin", Fms.Domain.Identity.FmsPermissions.MapPublish));
        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Engineer", Fms.Domain.Identity.FmsPermissions.MapPublish));
        Assert.False(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Operator", Fms.Domain.Identity.FmsPermissions.MapPublish));

        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Operator", Fms.Domain.Identity.FmsPermissions.OrdersCreate));
        Assert.False(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Operator", Fms.Domain.Identity.FmsPermissions.OrdersSkip));
        Assert.False(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Viewer", Fms.Domain.Identity.FmsPermissions.OrdersCreate));
        Assert.True(Fms.Domain.Identity.FmsPermissionCatalog.RoleHas("Viewer", Fms.Domain.Identity.FmsPermissions.Dashboard));

        // 2. 强制解锁安全互锁校验：必须实名且给出原因，杜绝匿名盲解
        var (safeAnon, errAnon) = identity.CheckSafetyInterlock("FORCE_RELEASE_MUTEX", engine, null, null);
        Assert.False(safeAnon);
        Assert.Contains("必须实名登记", errAnon);

        var (safeNamed, errNamed) = identity.CheckSafetyInterlock("FORCE_RELEASE_MUTEX", engine, "Supervisor_Zhang", "主路口双车对位传感器粘连排障");
        Assert.True(safeNamed);
        Assert.Null(errNamed);
    }

    [Fact]
    public void StorageManager_ChargeWithPayloadWarning_TracksCargoProperly()
    {
        var sm = new StorageManager();
        var zoneWip = new StorageZone("ZONE_WIP", "线边在制品库", StorageZoneType.Wip, capacity: 10);
        var slot = new StorageSlot("SLOT-WIP-01", "WIP 货位 1", "ZONE_WIP", StorageZoneType.Wip, "WB-01");
        sm.RegisterZone(zoneWip);
        sm.RegisterSlot(slot);

        // 货位放入物料
        var reserveDrop = slot.ReserveForDrop("ORD-101", "AMR-01");
        Assert.True(reserveDrop.Success);
        var confirmDrop = slot.ConfirmDrop("FOUP-TRAY-99");
        Assert.True(confirmDrop.Success);
        Assert.Equal(StorageSlotStatus.Occupied, slot.Status);

        // 提取物料后，车体载货
        var reservePick = slot.ReserveForPick("ORD-102", "AMR-01");
        Assert.True(reservePick.Success);
        var confirmPick = slot.ConfirmPick();
        Assert.True(confirmPick.Success);
        Assert.Equal(StorageSlotStatus.Empty, slot.Status);

        // 模拟电量告警回充：若人工锁定该货位维护，锁定状态独立生效
        slot.Lock("现场工位维护");
        Assert.True(slot.IsLocked);

        // 锁定时禁止任何新预约
        var reserveWhileLocked = slot.ReserveForDrop("ORD-103", "AMR-02");
        Assert.False(reserveWhileLocked.Success);
        Assert.Contains("锁定", reserveWhileLocked.Reason);

        slot.Unlock();
        Assert.False(slot.IsLocked);
    }
}
