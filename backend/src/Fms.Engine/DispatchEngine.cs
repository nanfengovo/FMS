using System.Collections.Concurrent;
using System.Diagnostics;
using Fms.Algorithm;
using Fms.Domain.Adapters;
using Fms.Domain.Logging;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Simulation;
using Fms.Domain.Storage;
using Fms.Domain.Topology;
using Fms.Domain.Traffic;
using Fms.Domain.Vehicles;
using Fms.Domain.Workflow;

namespace Fms.Engine;

public class EngineBenchmarkStats
{
    public long TotalTicks { get; set; }
    public double LastTickDurationMs { get; set; }
    public double AvgTickDurationMs { get; set; }
    public double MaxTickDurationMs { get; set; }
    public double P95TickDurationMs { get; set; }
    public int ActiveVehiclesCount { get; set; }
    public int ActiveOrdersCount { get; set; }
    public long AStarCallsCount { get; set; }
    public double AStarAvgDurationMs { get; set; }
    public long DeadlockChecksCount { get; set; }
    public double DeadlockCheckDurationMs { get; set; }
}

public class EngineBenchmarkReport
{
    public int VehicleCount { get; set; }
    public int TotalTicksExecuted { get; set; }
    public double MinTickDurationMs { get; set; }
    public double MaxTickDurationMs { get; set; }
    public double AvgTickDurationMs { get; set; }
    public double P95TickDurationMs { get; set; }
    public double P99TickDurationMs { get; set; }
    public long AStarRoutesPlanned { get; set; }
    public double AStarAvgDurationMs { get; set; }
    public long DeadlockChecksExecuted { get; set; }
    public double DeadlockCheckDurationMs { get; set; }
    public int ActiveOrdersFinished { get; set; }
    public long MemoryAllocatedMb { get; set; }
    public bool PassedRealtimeThreshold { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public partial class DispatchEngine
{
    public TopologyGraph Graph { get; } = new();
    public IEngineClock Clock { get; }

    public ConcurrentDictionary<string, VehicleAggregate> Vehicles { get; } = new();
    public ConcurrentDictionary<string, TransportOrderAggregate> Orders { get; } = new();

    // 结构化领域管理器
    public ResourceReservationManager ReservationManager { get; } = new();
    public StorageManager StorageManager { get; } = new();
    public RoadNetworkManager RoadNetworkManager { get; } = new();
    public WorkflowExecutionManager WorkflowManager { get; } = new();
    public IDecisionLogService? DecisionLogger { get; set; }

    public EngineBenchmarkStats BenchmarkStats { get; } = new();
    private readonly ConcurrentQueue<double> _recentTickDurations = new();
    private long _totalAStarDurationTicks = 0;

    // 资源锁定表 (线程安全映射: ZoneId -> VehicleId，边占用完全收敛由 ReservationManager 统一承载)
    private readonly ConcurrentDictionary<string, string> _zoneOccupancies = new();

    // 单线程命令队列：保证外部 HTTP 线程的指令入队并在调度 Tick 内有序安全执行，消除对象撕裂
    private readonly ConcurrentQueue<Action> _commandQueue = new();
    private readonly ConcurrentDictionary<string, List<long>> _orderScheduledEvents = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRechargeAttempts = new();

    private readonly AStarRouter _router;
    private readonly DeadlockDetector _deadlockDetector;
    private readonly AvoidanceSolver _avoidanceSolver;
    private readonly VirtualAmrDynamics _dynamics;
    private readonly NavigationTracker _navigationTracker = new();
    private Func<string, string>? _gatewaySignalReader;

    public void SetGatewaySignalReader(Func<string, string> reader) => _gatewaySignalReader = reader;

    public event EventHandler<VehicleTelemetry>? VehicleTelemetryUpdated;
    public event EventHandler<(TransportOrderAggregate Order, VehicleAggregate Vehicle)>? OrderStarted;
    public event EventHandler<(TransportOrderAggregate Order, VehicleAggregate Vehicle, string TargetNodeId)>? OrderArrivedTarget;
    public event EventHandler<(TransportOrderAggregate Order, VehicleAggregate Vehicle, OrderStep Step)>? OrderActionExecuting;
    public event EventHandler<TransportOrderAggregate>? OrderCompleted;
    public event EventHandler<TransportOrderAggregate>? OrderSubmitted;
    public event EventHandler<TransportOrderAggregate>? OrderCancelled;
    public event EventHandler<TransportOrderAggregate>? OrderFailed;
    public event EventHandler<TransportOrderAggregate>? OrderStepUpdated;
    public event EventHandler<string>? ConflictResolved;

    public const double HeartbeatTimeoutSeconds = 5.0;

    public DispatchEngine(IEngineClock? clock = null, IDecisionLogService? decisionLogger = null)
    {
        Clock = clock ?? new EngineClock();
        DecisionLogger = decisionLogger;
        _router = new AStarRouter();
        _deadlockDetector = new DeadlockDetector();
        _avoidanceSolver = new AvoidanceSolver(_router);
        _dynamics = new VirtualAmrDynamics();
    }

    public void RegisterVehicle(VehicleAggregate vehicle)
    {
        vehicle.LastHeartbeatUtc = Clock.CurrentTimeUtc;
        BindVehicleToCurrentMapIfEmpty(vehicle);
        SnapVehicleToNearestNodeIfNeeded(vehicle);
        SnapHomeNodeIfNeeded(vehicle);
        Vehicles[vehicle.Id] = vehicle;
    }

    /// <summary>启用且通信在线、空闲、无急停/暂停、未挂单，才允许承接新单。</summary>
    public static bool IsDispatchEligible(VehicleAggregate v) =>
        v.IsEnabled && v.IsOnline && !v.IsEstop && !v.IsPaused &&
        v.Status == VehicleStatus.Idle &&
        string.IsNullOrEmpty(v.AssignedOrderId);

    public void BindVehiclesToCurrentMapIfEmpty()
    {
        foreach (var v in Vehicles.Values)
        {
            BindVehicleToCurrentMapIfEmpty(v);
            SnapVehicleToNearestNodeIfNeeded(v);
            SnapHomeNodeIfNeeded(v);
        }
    }

    public void BindVehicleToCurrentMapIfEmpty(VehicleAggregate vehicle)
    {
        if (!string.IsNullOrWhiteSpace(vehicle.MapId)) return;
        var mapName = string.IsNullOrWhiteSpace(Graph.Name) ? "DefaultMap" : Graph.Name;
        vehicle.MapId = mapName;
        vehicle.MapName = mapName;
    }

    /// <summary>无当前点或点已从路网消失时，按无限半径吸附到最近站点（与派单起点吸附一致）。</summary>
    public void SnapVehicleToNearestNodeIfNeeded(VehicleAggregate vehicle)
    {
        if (Graph.Nodes.Count == 0) return;
        if (!string.IsNullOrEmpty(vehicle.CurrentNodeId) && Graph.GetNode(vehicle.CurrentNodeId) != null)
        {
            return;
        }

        var snap = Graph.FindNearestNode(vehicle.Pose.Position, double.MaxValue);
        if (snap != null)
        {
            vehicle.CurrentNodeId = snap.Id;
        }
    }

    public void SnapHomeNodeIfNeeded(VehicleAggregate vehicle)
    {
        if (Graph.Nodes.Count == 0) return;
        if (!string.IsNullOrEmpty(vehicle.HomeNodeId) && Graph.GetNode(vehicle.HomeNodeId) != null)
        {
            return;
        }

        var home = FindNearestHomeNode(vehicle.Pose.Position);
        if (home != null)
        {
            vehicle.HomeNodeId = home.Id;
        }
    }

    public MapNode? FindNearestHomeNode(Point2D position)
    {
        MapNode? best = null;
        double bestDist = double.MaxValue;
        foreach (var node in Graph.Nodes)
        {
            if (node.Type != NodeType.StandbyPoint && node.Type != NodeType.ChargePoint) continue;
            double d = node.Position.DistanceTo(position);
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }
        return best;
    }

    public bool SetVehicleEnabled(string vehicleId, bool enabled)
    {
        if (!Vehicles.TryGetValue(vehicleId, out var vehicle)) return false;
        vehicle.IsEnabled = enabled;
        DecisionLogger?.Record(
            DecisionCategory.OrderDispatch,
            vehicle.Id,
            enabled ? "EnableVehicle" : "DisableVehicle",
            "Accepted",
            enabled ? "车辆已启用，可承接新单" : "车辆已停用，不再派发新单");
        return true;
    }

    public bool UpdateVehicleLedger(
        string vehicleId,
        string? name = null,
        string? vehicleType = null,
        string? driveType = null,
        double? length = null,
        double? width = null,
        double? maxSpeed = null,
        string? homeNodeId = null)
    {
        if (!Vehicles.TryGetValue(vehicleId, out var vehicle)) return false;

        if (!string.IsNullOrWhiteSpace(name)) vehicle.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(vehicleType)) vehicle.VehicleType = vehicleType.Trim();
        if (!string.IsNullOrWhiteSpace(driveType) && Enum.TryParse<Fms.Domain.Shared.Enums.DriveType>(driveType, true, out var parsedDrive))
        {
            vehicle.Kinematics.DriveType = parsedDrive;
        }
        if (length.HasValue && length.Value > 0) vehicle.Kinematics.Length = length.Value;
        if (width.HasValue && width.Value > 0) vehicle.Kinematics.Width = width.Value;
        if (maxSpeed.HasValue && maxSpeed.Value > 0) vehicle.Kinematics.MaxSpeed = maxSpeed.Value;
        if (homeNodeId != null)
        {
            if (homeNodeId.Length == 0)
            {
                vehicle.HomeNodeId = null;
            }
            else
            {
                var node = Graph.GetNode(homeNodeId);
                if (node == null) return false;
                if (node.Type != NodeType.StandbyPoint && node.Type != NodeType.ChargePoint) return false;
                vehicle.HomeNodeId = node.Id;
            }
        }

        return true;
    }

    private void ScheduleOrderEvent(string orderId, long triggerTimeMs, Action callback)
    {
        long eventId = Clock.ScheduleEvent(triggerTimeMs, () =>
        {
            // 订单一旦进入终态（已完成/已失败/已取消），排期中的延迟事件一律作废
            if (!Orders.TryGetValue(orderId, out var o) || o.IsFinished)
            {
                return;
            }
            callback();
        });

        _orderScheduledEvents.AddOrUpdate(orderId,
            _ => new List<long> { eventId },
            (_, list) => { lock (list) { list.Add(eventId); return list; } });
    }

    private void CancelScheduledEventsForOrder(string orderId)
    {
        if (_orderScheduledEvents.TryRemove(orderId, out var list))
        {
            lock (list)
            {
                foreach (var id in list)
                {
                    Clock.CancelEvent(id);
                }
            }
        }
    }

    /// <summary>
    /// 把外部系统/界面传入的车辆标识解析为引擎内真实车辆 ID。
    /// 依次匹配：完整 ID、车辆名称/编码、尾部数字（"25" / "AMR-25" 命中 "BENCH-AMR-25"）。
    /// 找不到或有歧义时返回 null。
    /// </summary>
    public string? ResolveVehicleId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim();

        if (Vehicles.ContainsKey(key)) return key;

        var byName = Vehicles.Values
            .Where(v => string.Equals(v.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Code, key, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Id, key, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Id)
            .Distinct()
            .ToList();
        if (byName.Count == 1) return byName[0];

        var digits = new string(key.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (digits.Length == 0) return null;
        var number = int.Parse(digits);

        var prefix = key[..^digits.Length].TrimEnd('-', '_', ' ');
        var candidates = Vehicles.Values.Where(v =>
        {
            var vd = new string(v.Id.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (vd.Length == 0 || int.Parse(vd) != number) return false;
            return prefix.Length == 0 || v.Id.Contains(prefix, StringComparison.OrdinalIgnoreCase);
        }).Select(v => v.Id).ToList();

        // 带前缀（如 AMR）仍无命中时放宽为纯数字匹配
        if (candidates.Count == 0 && prefix.Length > 0)
        {
            candidates = Vehicles.Values.Where(v =>
            {
                var vd = new string(v.Id.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                return vd.Length > 0 && int.Parse(vd) == number;
            }).Select(v => v.Id).ToList();
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    public bool HasNode(string? nodeId) => !string.IsNullOrEmpty(nodeId) && Graph.GetNode(nodeId) != null;

    /// <summary>
    /// 货位绑定的站点必须是当前路网上真实存在的站点。装载新地图后，将失效的绑定重新落到地图站点上：
    /// 充电类货位落到充电点，其余货位落到待命点，并在路网上尽量分散。
    /// </summary>
    public int RebindStorageToGraph()
    {
        var nodes = Graph.Nodes;
        if (nodes.Count == 0) return 0;

        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        var stale = StorageManager.GetAllSlots().Where(s => !nodeIds.Contains(s.BindNodeId)).ToList();
        if (stale.Count == 0)
        {
            RelabelDemoSlots();
            return 0;
        }

        // 以路网连通性筛选：只在最大连通分量内选站，保证车辆能开过去
        var parent = nodes.ToDictionary(n => n.Id, n => n.Id);
        string Find(string x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        foreach (var p in Graph.Paths)
        {
            if (parent.ContainsKey(p.FromNodeId) && parent.ContainsKey(p.ToNodeId))
                parent[Find(p.FromNodeId)] = Find(p.ToNodeId);
        }
        var mainRoot = nodes.GroupBy(n => Find(n.Id)).OrderByDescending(g => g.Count()).First().Key;
        var inNet = nodes.Where(n => Find(n.Id) == mainRoot).ToList();

        var standby = inNet.Where(n => n.Type == NodeType.StandbyPoint).OrderBy(n => n.Id).ToList();
        if (standby.Count == 0) standby = inNet.Where(n => n.Type == NodeType.PullOverPoint).OrderBy(n => n.Id).ToList();
        if (standby.Count == 0) standby = inNet.Where(n => n.Type == NodeType.JobPoint).OrderBy(n => n.Id).ToList();
        var charge = inNet.Where(n => n.Type == NodeType.ChargePoint).OrderBy(n => n.Id).ToList();
        if (standby.Count == 0) standby = inNet.OrderBy(n => n.Id).ToList();
        if (charge.Count == 0) charge = standby;

        // 贪心最远点采样，让不同库区的站点散布在整张图上
        var chosen = new List<MapNode>();
        MapNode Pick(List<MapNode> pool)
        {
            var avail = pool.Where(n => chosen.All(c => c.Id != n.Id)).ToList();
            if (avail.Count == 0) avail = pool;
            if (chosen.Count == 0) return avail[avail.Count / 2];
            return avail.OrderByDescending(n => chosen.Min(c => c.Position.DistanceTo(n.Position))).First();
        }

        int changed = 0;
        foreach (var group in stale.GroupBy(s => s.BindNodeId).OrderBy(g => g.Key))
        {
            bool isCharge = group.Count(s => s.ZoneType == StorageZoneType.Charge) * 2 > group.Count();
            var node = Pick(isCharge ? charge : standby);
            chosen.Add(node);
            foreach (var slot in group)
            {
                slot.BindNodeId = node.Id;
                changed++;
            }
            DecisionLogger?.Record(DecisionCategory.StorageAllocation, "StorageManager", "RebindToGraph", "Success",
                $"货位绑定 {group.Key} 不在当前路网，已重新绑定到站点 {node.Id}（{group.Count()} 个货位）");
        }
        RelabelDemoSlots();
        return changed;
    }

    private void RelabelDemoSlots()
    {
        foreach (var slot in StorageManager.GetAllSlots())
        {
            string bind = string.IsNullOrWhiteSpace(slot.BindNodeId) ? "未绑定" : slot.BindNodeId;
            slot.Name = $"演示货位 {slot.Id}（绑定 {bind}）";
        }
    }

    public void SubmitOrder(TransportOrderAggregate order)
    {
        // 指定车辆号归一化为真实车辆 ID（外部系统常用 "25" / "AMR-25" 这类编号）
        if (!string.IsNullOrEmpty(order.BindVehicleId))
        {
            order.BindVehicleId = ResolveVehicleId(order.BindVehicleId) ?? order.BindVehicleId;
        }

        // 自动解析货位与物理停靠点 (SourceSlotId -> BindNodeId, TargetSlotId -> BindNodeId)
        if (!string.IsNullOrEmpty(order.SourceSlotId))
        {
            var srcSlot = StorageManager.GetSlot(order.SourceSlotId);
            if (srcSlot != null && order.Steps.Count > 0)
            {
                order.Steps[0].TargetNodeId = srcSlot.BindNodeId;
                order.Steps[0].TargetSlotId = srcSlot.Id;
            }
        }

        // 若任务需要放货且尚未分配目标货位，自动调用 StorageManager 为其分配空闲货位
        // 放货工步已显式指定站点时，尊重调用方的目标：只在该站点上预约一个空闲货位，不改写目标站点
        var explicitDrop = order.Steps.FirstOrDefault(s => s.Action == ActionType.PutDown && !string.IsNullOrEmpty(s.TargetNodeId) && string.IsNullOrEmpty(s.TargetSlotId));
        if (string.IsNullOrEmpty(order.TargetSlotId) && explicitDrop != null)
        {
            var boundSlot = StorageManager.GetAllSlots()
                .Where(s => s.BindNodeId == explicitDrop.TargetNodeId && s.Status == StorageSlotStatus.Empty)
                .OrderBy(s => s.Id)
                .FirstOrDefault(s => s.ReserveForDrop(order.Id, order.BindVehicleId ?? "PENDING").Success);
            if (boundSlot != null)
            {
                order.TargetSlotId = boundSlot.Id;
                order.Storage = boundSlot.Id;
                order.Target = boundSlot.Id;
                explicitDrop.TargetSlotId = boundSlot.Id;
                DecisionLogger?.Record(DecisionCategory.StorageAllocation, order.Id, "ReserveSlotAtTarget", "Success", $"在目标站点 {boundSlot.BindNodeId} 预约货位 {boundSlot.Id}");
            }
        }
        else if (string.IsNullOrEmpty(order.TargetSlotId) && order.Steps.Any(s => s.Action == ActionType.PutDown))
        {
            var zType = order.TaskType.Equals("Finished", StringComparison.OrdinalIgnoreCase) ? StorageZoneType.Finished : StorageZoneType.Wip;
            var (allocOk, allocReason, allocSlot) = StorageManager.AllocateSlotForDrop(zType, order.Id, order.AssignedVehicleId ?? "PENDING");
            if (allocOk && allocSlot != null)
            {
                order.TargetSlotId = allocSlot.Id;
                order.Storage = allocSlot.Id;
                order.Target = allocSlot.Id;
                foreach (var step in order.Steps.Where(s => s.Action == ActionType.PutDown || (s.Action == ActionType.Move && string.IsNullOrEmpty(s.TargetNodeId))))
                {
                    step.TargetNodeId = allocSlot.BindNodeId;
                    step.TargetSlotId = allocSlot.Id;
                }
                DecisionLogger?.Record(DecisionCategory.StorageAllocation, order.Id, "AllocateSlotForDrop", "Success", allocReason);
            }
        }
        else if (!string.IsNullOrEmpty(order.TargetSlotId))
        {
            var targetSlot = StorageManager.GetSlot(order.TargetSlotId);
            if (targetSlot != null)
            {
                foreach (var step in order.Steps.Where(s => s.Action == ActionType.PutDown || (s.Action == ActionType.Move && string.IsNullOrEmpty(s.TargetNodeId))))
                {
                    step.TargetNodeId = targetSlot.BindNodeId;
                    step.TargetSlotId = targetSlot.Id;
                }
            }
        }

        Orders[order.Id] = order;
        OrderSubmitted?.Invoke(this, order);
        _commandQueue.Enqueue(() =>
        {
            TryDispatchOrders();
        });
    }

    /// <summary>
    /// 系统重启时从 SQLite 数据库装载并恢复订单执行上下文
    /// </summary>
    public void RestoreOrder(TransportOrderAggregate order)
    {
        Orders[order.Id] = order;

        // 已结束的订单只作为历史记录登记：不再挂回车辆，不再参与调度，状态保持落库时的样子
        if (order.IsFinished) return;

        _commandQueue.Enqueue(() =>
        {
            if (order.IsFinished) return;

            if (!string.IsNullOrEmpty(order.AssignedVehicleId) &&
                Vehicles.TryGetValue(order.AssignedVehicleId, out var vehicle) &&
                (string.IsNullOrEmpty(vehicle.AssignedOrderId) || vehicle.AssignedOrderId == order.Id))
            {
                vehicle.AssignedOrderId = order.Id;
                if (order.Status == OrderStatus.Running || order.Status == OrderStatus.WaitingAction || order.Status == OrderStatus.Assigned)
                {
                    ExecuteNextStep(vehicle, order);
                }
            }
            else
            {
                // 原执行车辆不存在或已被别的订单占用：回到排队，等待重新派车
                if (order.Status != OrderStatus.Queued)
                {
                    order.AssignedVehicleId = null;
                    order.RestoreStatus(OrderStatus.Queued);
                }
                TryDispatchOrders();
            }
        });
    }

    public ConcurrentDictionary<string, string> IoSignals { get; } = new(new[]
    {
        new KeyValuePair<string, string>("DOCK_READY", "0"),
        new KeyValuePair<string, string>("LINE_CLEAR", "0"),
        new KeyValuePair<string, string>("MATERIAL_ARRIVED", "0")
    });

    public event Action<string, string>? IoSignalChanged;
    public void SetIoSignal(string key, string val)
    {
        IoSignals[key] = val;
        IoSignalChanged?.Invoke(key, val);
    }
    public string GetIoSignal(string key) => IoSignals.TryGetValue(key, out var v) ? v : "0";

    // 取消 / 跳步 / 重做 / 中止 / 暂停 / 重试 / 回退等订单人工操作见 DispatchEngine.OrderCommands.cs

    public void SetVehicleEstop(string vehicleId, bool isEstop)
    {
        _commandQueue.Enqueue(() =>
        {
            if (Vehicles.TryGetValue(vehicleId, out var vehicle))
            {
                vehicle.IsEstop = isEstop;
                if (isEstop)
                {
                    vehicle.Status = VehicleStatus.Fault;
                    vehicle.CurrentSpeed = 0.0;
                }
                else
                {
                    vehicle.Status = VehicleStatus.Idle;
                }
            }
        });
    }

    public void SetVehicleBattery(string vehicleId, double socPercent)
    {
        _commandQueue.Enqueue(() =>
        {
            if (Vehicles.TryGetValue(vehicleId, out var vehicle))
            {
                vehicle.Bms.SocPercent = Math.Clamp(socPercent, 0.0, 100.0);
            }
        });
    }

    public void InjectVehicleFault(string vehicleId, string faultType)
    {
        _commandQueue.Enqueue(() =>
        {
            if (Vehicles.TryGetValue(vehicleId, out var v))
            {
                var fault = (faultType ?? "").Trim().ToLowerInvariant();
                if (fault is "estop")
                {
                    v.IsEstop = true;
                    v.Status = VehicleStatus.Fault;
                    v.CurrentSpeed = 0.0;
                }
                else if (fault is "pause" or "alarm")
                {
                    v.IsPaused = true;
                    v.CurrentSpeed = 0.0;
                }
                else if (fault is "battery_depleted" or "lowbattery")
                {
                    v.Bms.SocPercent = 5.0;
                }
                else if (fault is "clear" or "clearfault")
                {
                    v.IsEstop = false;
                    v.IsPaused = false;
                    v.Status = VehicleStatus.Idle;
                    if (v.Bms.SocPercent < 20) v.Bms.SocPercent = 100.0;
                }
            }
        });
    }

    // 主调度循环 Tick (通常每 100ms 调用一次)
    public void AdvanceTick(double dtSec)
    {
        var tickSw = Stopwatch.StartNew();

        // 0. 执行外部指令队列
        while (_commandQueue.TryDequeue(out var cmd))
        {
            try
            {
                cmd();
            }
            catch (Exception ex)
            {
                DecisionLogger?.Record(
                    DecisionCategory.OrderDispatch,
                    "CommandQueue",
                    "ExecuteCommand",
                    "Exception",
                    $"异步指令执行抛出异常: {ex.Message}");
            }
        }

        // 1. 严格时钟控制：仿真时钟暂停时，整场物理运动与时空逻辑完全冻结
        if (Clock.IsPaused)
        {
            tickSw.Stop();
            return;
        }

        // 异常矩阵：检查车辆通信心跳 (超时 > 5.0 秒判定掉线，挂起任务并在原位维持路权防撞)
        CheckVehicleHeartbeats();

        // 根据仿真倍速缩放动力学步长，确保 2x/5x 倍速时车辆运行真实加速
        double effectiveDt = dtSec * Clock.TimeScale;
        Clock.AdvanceFixedTick((long)(dtSec * 1000));

        // 2. 车辆动力学与路径前瞻预约
        foreach (var vehicle in Vehicles.Values)
        {
            AccumulateOrderMetrics(vehicle, effectiveDt);

            if (vehicle.Status == VehicleStatus.Running)
            {
                // 离网车辆驶回路网入口期间不评估横向偏差；到达首个轨迹点后恢复正常跟踪
                if (vehicle.IsRejoining && (vehicle.TrajectoryPoints.Count == 0 || vehicle.CurrentTrajectoryIndex >= 1))
                {
                    vehicle.IsRejoining = false;
                }

                // 实时高频跟踪横向与航向偏差
                if (vehicle.TrajectoryPoints.Count > 0 && !vehicle.IsRejoining)
                {
                    var currentSegment = vehicle.CurrentItinerary?.GetSegmentByPathId(vehicle.CurrentPathId ?? "");
                    var targetPt = vehicle.TrajectoryPoints.Last();
                    var trackingReport = _navigationTracker.EvaluateTracking(
                        vehicle,
                        currentSegment,
                        targetPt,
                        vehicle.CurrentItinerary?.TargetHeadingRad
                    );
                    vehicle.LastNavigationReport = trackingReport;

                    if (trackingReport.RequiresReroute)
                    {
                        DecisionLogger?.Record(
                            DecisionCategory.Routing,
                            vehicle.Id,
                            "LateralDeviationExcessive",
                            "RerouteTriggered",
                            trackingReport.Message ?? $"横向偏差 {trackingReport.LateralDeviationMeters:F2}m 超过安全阈值，触发重规划");

                        TriggerDynamicReroute(vehicle, "横向偏差超限重规划");
                    }
                }

                // 检查跟驰模型 (Car-Following): 避免同向跟车追尾与对向防撞
                UpdateCarFollowing(vehicle, effectiveDt);

                // 检查互斥区准入申请 (Mutex Zone Claim)
                if (!CheckMutexZoneAdmittance(vehicle))
                {
                    continue;
                }

                // 检查航道占用锁准入 (Path Reservation Admittance)
                if (!CheckPathAdmittance(vehicle))
                {
                    continue;
                }

                // 物理积分更新
                _dynamics.StepSimulation(vehicle, effectiveDt, Clock.CurrentTimeUtc);
                UpdateCurrentPathAndNodes(vehicle);
            }
            else if (vehicle.Status == VehicleStatus.Charging)
            {
                _dynamics.StepSimulation(vehicle, effectiveDt, Clock.CurrentTimeUtc);

                // 检查充电是否达到 85.0% 充满阈值
                if (vehicle.Bms.SocPercent >= 85.0)
                {
                    vehicle.Status = VehicleStatus.Idle;
                    vehicle.Bms.IsCharging = false;

                    if (!string.IsNullOrEmpty(vehicle.AssignedOrderId) &&
                        Orders.TryGetValue(vehicle.AssignedOrderId, out var chgOrder))
                    {
                        if (!chgOrder.IsFinished && chgOrder.CurrentStep?.Action == ActionType.Charge)
                        {
                            chgOrder.AdvanceStep(Clock.CurrentTimeUtc);
                            if (chgOrder.Status == OrderStatus.Done)
                            {
                                OrderCompleted?.Invoke(this, chgOrder);
                                vehicle.AssignedOrderId = null;
                                ReleaseAllReservations(vehicle);
                                vehicle.ClearRoute();
                            }
                            else if (!chgOrder.IsFinished)
                            {
                                ExecuteNextStep(vehicle, chgOrder);
                            }
                        }
                    }
                }
            }

            // 检查避让让行车恢复原任务 (Spur Resume)
            CheckYieldingVehicleResume(vehicle);

            // 广播高频遥测
            EmitTelemetry(vehicle);
        }

        // 3. 检查工位到达与工序步骤推进
        CheckStepCompletion();

        // 4. 前瞻对冲死锁检测与原子避让仲裁
        ArbitrateTraffic();
        BreakBumperDeadlocks();

        // 5. 自动巡检低电量车辆并派发回充工单
        CheckAutonomousBatteryRecharge();

        // 5.1 回收已结束（取消/失败/完成）订单仍占用的车辆，避免车辆被"幽灵订单"永久占住
        ReleaseOrphanedAssignments();

        // 6. 驱动任务派发队列
        TryDispatchOrders();

        // 6.1 驱动低代码业务流实例状态机 (直接从工业网关采集信号)
        var simNow = Clock.CurrentTimestampMs;
        foreach (var instance in WorkflowManager.GetAllInstances())
        {
            instance.ObservedSimMs = simNow;
            if (instance.StartSimMs == 0) instance.StartSimMs = simNow;
        }
        WorkflowManager.AdvanceInstances(
            signalName => _gatewaySignalReader != null ? _gatewaySignalReader(signalName) : GetIoSignal(signalName),
            vId => Vehicles.GetValueOrDefault(vId),
            oId => Orders.GetValueOrDefault(oId),
            SubmitOrder
        );

        // 7. 维持在线虚拟车辆仿真遥测心跳
        foreach (var v in Vehicles.Values)
        {
            if (v.IsOnline && v.AutoSimulateHeartbeat)
            {
                v.LastHeartbeatUtc = Clock.CurrentTimeUtc;
            }
        }

        tickSw.Stop();
        double tickMs = tickSw.Elapsed.TotalMilliseconds;
        BenchmarkStats.TotalTicks++;
        BenchmarkStats.LastTickDurationMs = Math.Round(tickMs, 2);
        BenchmarkStats.ActiveVehiclesCount = Vehicles.Count;
        BenchmarkStats.ActiveOrdersCount = Orders.Values.Count(o => !o.IsFinished);

        _recentTickDurations.Enqueue(tickMs);
        while (_recentTickDurations.Count > 100) _recentTickDurations.TryDequeue(out _);

        var samples = _recentTickDurations.ToArray();
        if (samples.Length > 0)
        {
            BenchmarkStats.AvgTickDurationMs = Math.Round(samples.Average(), 2);
            BenchmarkStats.MaxTickDurationMs = Math.Round(samples.Max(), 2);
            Array.Sort(samples);
            int p95Idx = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);
            BenchmarkStats.P95TickDurationMs = Math.Round(samples[p95Idx], 2);
        }
    }

    /// <summary>
    /// 车辆当前所在的航道。有结构化行程时按展开后的采样点区间精确对应；
    /// 否则退化为按进度比例估算（避让支线等无行程的临时路线）。
    /// </summary>
    private static string ResolveCurrentPathId(VehicleAggregate vehicle)
    {
        var itinerary = vehicle.CurrentItinerary;
        if (itinerary != null && itinerary.Segments.Count > 0)
        {
            int idx = vehicle.CurrentTrajectoryIndex;
            int acc = 0;
            ItinerarySegment? last = null;
            foreach (var seg in itinerary.Segments)
            {
                int count = seg.TrajectoryPoints.Count;
                int contributed = acc > 0 && count > 0 ? count - 1 : count;
                if (!string.IsNullOrEmpty(seg.PathId)) last = seg;
                if (idx < acc + contributed && !string.IsNullOrEmpty(seg.PathId))
                {
                    return seg.PathId;
                }
                acc += contributed;
            }
            if (last != null) return last.PathId;
        }

        double progress = (double)vehicle.CurrentTrajectoryIndex / Math.Max(1, vehicle.TrajectoryPoints.Count);
        int pathIdx = Math.Min(vehicle.RoutePathIds.Count - 1, (int)(progress * vehicle.RoutePathIds.Count));
        return vehicle.RoutePathIds[pathIdx];
    }

    private void ReleaseOrphanedAssignments()
    {
        foreach (var vehicle in Vehicles.Values)
        {
            // 没有任务、没有驻留计时的"暂停"车辆（例如重启后从库里恢复）回到待命
            if (string.IsNullOrEmpty(vehicle.AssignedOrderId))
            {
                if (vehicle.Status == VehicleStatus.Paused && !vehicle.IsPaused && !vehicle.IsEstop && vehicle.WaitUntilTime == null)
                {
                    vehicle.Status = VehicleStatus.Idle;
                    vehicle.CurrentSpeed = 0.0;
                }
                continue;
            }
            if (vehicle.IsYielding) continue;

            bool orphaned = !Orders.TryGetValue(vehicle.AssignedOrderId, out var order) || order.IsFinished;
            if (!orphaned) continue;

            var orderId = vehicle.AssignedOrderId;
            if (order != null) CancelScheduledEventsForOrder(order.Id);
            ReleaseAllReservations(vehicle);
            vehicle.ClearRoute();
            vehicle.WaitUntilTime = null;
            vehicle.AssignedOrderId = null;
            DecisionLogger?.Record(DecisionCategory.OrderDispatch, vehicle.Id, "ReleaseVehicle", "Success",
                $"订单 {order?.OrderSerial ?? orderId} 已{(order == null ? "不存在" : order.Status switch { OrderStatus.Cancelled => "取消", OrderStatus.Failed => "失败", _ => "结束" })}，车辆已释放并恢复待命");
        }
    }

    private bool CheckPathAdmittance(VehicleAggregate vehicle)
    {
        if (vehicle.RoutePathIds.Count == 0 || vehicle.TrajectoryPoints.Count == 0) return true;

        string targetPathId = ResolveCurrentPathId(vehicle);

        if (vehicle.CurrentPathId != targetPathId)
        {
            var path = Graph.GetPath(targetPathId);
            if (path == null) return false;

            double remainingDist = (vehicle.TrajectoryPoints.Count - vehicle.CurrentTrajectoryIndex) * 0.25;
            var order = !string.IsNullOrEmpty(vehicle.AssignedOrderId) && Orders.TryGetValue(vehicle.AssignedOrderId, out var o) ? o : null;

            var segment = vehicle.CurrentItinerary?.GetSegmentByPathId(targetPathId) ?? new ItinerarySegment
            {
                PathId = targetPathId,
                FromNodeId = path.FromNodeId,
                ToNodeId = path.ToNodeId,
                MotionMode = vehicle.IsMovingBackward ? SegmentMotionMode.Backward : SegmentMotionMode.Forward,
                RoadWidth = path.RoadWidth,
                MaxAllowedSpeed = path.MaxSpeed,
                RecommendedSpeed = path.MaxSpeed,
                EstimatedDurationSec = path.Length / Math.Max(0.5, path.MaxSpeed)
            };

            var claimResult = ReservationManager.TryClaimSegment(vehicle, segment, Clock.CurrentTimeUtc, remainingDist, order, Graph);

            if (!claimResult.Success)
            {
                vehicle.CurrentSpeed = 0.0;
                vehicle.BlockedByVehicleId = claimResult.BlockedByVehicleId;
                // 被拒原因没变化时不重复记录，避免每个 Tick 刷一条把有用的决策淹没
                if (!_lastClaimDenial.TryGetValue(vehicle.Id, out var lastDenial) || lastDenial != claimResult.Reason)
                {
                    _lastClaimDenial[vehicle.Id] = claimResult.Reason;
                    DecisionLogger?.Record(DecisionCategory.ResourceLease, vehicle.Id, "ClaimPathSegment", "Denied", claimResult.Reason);
                }
                return false;
            }

            _lastClaimDenial.TryRemove(vehicle.Id, out _);

            if (claimResult.IsCarFollowing)
            {
                vehicle.CarFollowingTargetVehicleId = claimResult.BlockedByVehicleId;
                if (!_lastFollowTarget.TryGetValue(vehicle.Id, out var lastFollow) || lastFollow != claimResult.BlockedByVehicleId)
                {
                    _lastFollowTarget[vehicle.Id] = claimResult.BlockedByVehicleId ?? "";
                    DecisionLogger?.Record(DecisionCategory.TrafficAvoidance, vehicle.Id, "CarFollowing", "Approved", "同向跟驰中");
                }
            }
            else
            {
                vehicle.CarFollowingTargetVehicleId = null;
                _lastFollowTarget.TryRemove(vehicle.Id, out _);
            }

            // 准入通过，释放旧路段并占有新路段
            if (!string.IsNullOrEmpty(vehicle.CurrentPathId))
            {
                ReleasePath(vehicle.CurrentPathId, vehicle.Id);
            }

            vehicle.CurrentPathId = targetPathId;
            vehicle.ReservedPathIds.Add(targetPathId);
            vehicle.BlockedByVehicleId = null;
        }

        return true;
    }

    private void UpdateCurrentPathAndNodes(VehicleAggregate vehicle)
    {
        var nearNode = Graph.FindNearestNode(vehicle.Pose.Position, 1.2);
        if (nearNode != null)
        {
            vehicle.CurrentNodeId = nearNode.Id;
        }
    }

    private void ReleasePath(string pathId, string vehicleId)
    {
        ReservationManager.ReleaseSegment(vehicleId, pathId);
    }

    public void ReleaseAllReservations(VehicleAggregate vehicle)
    {
        ReservationManager.ReleaseAll(vehicle.Id);
        if (!string.IsNullOrEmpty(vehicle.CurrentPathId))
        {
            ReleasePath(vehicle.CurrentPathId, vehicle.Id);
        }

        foreach (var pId in vehicle.ReservedPathIds)
        {
            ReleasePath(pId, vehicle.Id);
        }
        vehicle.ReservedPathIds.Clear();

        if (!string.IsNullOrEmpty(vehicle.ClaimedMutexZoneId))
        {
            ReservationManager.ReleaseZone(vehicle.Id, vehicle.ClaimedMutexZoneId);
            var zone = Graph.Zones.FirstOrDefault(z => z.Id == vehicle.ClaimedMutexZoneId);
            zone?.Release(vehicle.Id);
            _zoneOccupancies.TryRemove(vehicle.ClaimedMutexZoneId, out _);
            vehicle.ClaimedMutexZoneId = null;
        }
    }

    private void UpdateCarFollowing(VehicleAggregate follower, double dtSec)
    {
        follower.CarFollowingTargetSpeed = null;

        // 死锁破除窗口内：对已经静止的车辆放行，避免两车互相被对方的防撞红线永久拦住
        bool bypassing = follower.BumperOverrideUntil.HasValue && Clock.CurrentTimeUtc <= follower.BumperOverrideUntil.Value;
        if (!bypassing) follower.BumperOverrideUntil = null;

        var otherRunningVehicles = Vehicles.Values
            .Where(v => v.Id != follower.Id && v.Status != VehicleStatus.Charging)
            .ToList();

        foreach (var other in otherRunningVehicles)
        {
            // 没有任务的待命车辆视为停在车位/泊位里，不构成走廊上的移动障碍，
            // 否则任何一台收工后原地待命的车都会把整条通道永久堵死
            if (string.IsNullOrEmpty(other.AssignedOrderId) && other.Status != VehicleStatus.Running) continue;
            if (bypassing && other.CurrentSpeed < 0.05) continue;

            double dist = follower.Pose.DistanceTo(other.Pose);
            if (dist > 8.0) continue;

            double fDx = Math.Cos(follower.Pose.ThetaRad);
            double fDy = Math.Sin(follower.Pose.ThetaRad);

            // 检查 other 是否在 follower 前方走廊视野内
            double relX = other.Pose.Position.X - follower.Pose.Position.X;
            double relY = other.Pose.Position.Y - follower.Pose.Position.Y;
            double forwardProj = fDx * relX + fDy * relY;
            double crossProj = Math.Abs(-fDy * relX + fDx * relY);

            // 前方走廊 1.0 米宽度范围内
            if (forwardProj > 0 && crossProj < 1.0)
            {
                // 1. 物理激光防撞绝对安全红线 (Hardware Laser Bumper)
                if (dist < 1.0)
                {
                    follower.CarFollowingTargetVehicleId = other.Id;
                    follower.CarFollowingTargetSpeed = 0.0;
                    break;
                }

                // 2. 优势车礼让避让车拐入侧线：若对方正在让行，优势车提前在道岔外驻车等候出清
                if (other.IsYielding && !follower.IsYielding && dist < 18.0)
                {
                    follower.CarFollowingTargetVehicleId = other.Id;
                    follower.CarFollowingTargetSpeed = 0.0;
                    break;
                }

                double oDx = Math.Cos(other.Pose.ThetaRad);
                double oDy = Math.Sin(other.Pose.ThetaRad);
                double dot = fDx * oDx + fDy * oDy;

                // 3. 只有同向跟车 (dot > 0.5) 才进入平滑跟驰调速
                if (dot > 0.5 && dist < 6.0)
                {
                    follower.CarFollowingTargetVehicleId = other.Id;
                    follower.CarFollowingTargetSpeed = Math.Max(0.2, other.CurrentSpeed * 0.9);
                    break;
                }
            }
        }

        bool stoppedByBumper = follower.CarFollowingTargetSpeed is < 0.01 && follower.CurrentSpeed < 0.05;
        follower.BumperStallSeconds = stoppedByBumper ? follower.BumperStallSeconds + dtSec : 0.0;
    }

    private const double BumperStallThresholdSec = 12.0;
    private const double BumperChainStallThresholdSec = 45.0;
    private const double BumperOverrideSec = 8.0;

    private string? BumperBlockerOf(VehicleAggregate vehicle) =>
        vehicle.Status == VehicleStatus.Running && vehicle.CarFollowingTargetSpeed is < 0.01 ? vehicle.CarFollowingTargetVehicleId : null;

    /// <summary>车辆此刻停下来是在等谁：路权/互斥区占用者，或挡在前面的车辆（用于大屏提示和排障）。</summary>
    public string? GetBlockerId(VehicleAggregate vehicle)
    {
        if (vehicle.Status != VehicleStatus.Running || vehicle.CurrentSpeed >= 0.05) return null;
        return vehicle.BlockedByVehicleId ?? BumperBlockerOf(vehicle);
    }

    private readonly ConcurrentDictionary<string, string> _lastClaimDenial = new();
    private readonly ConcurrentDictionary<string, string> _lastFollowTarget = new();

    /// <summary>
    /// 防撞红线死锁破除：两车（或多车成环）互相被对方的激光红线拦停时，
    /// 按订单优先级（再按编号）放行一台，让它短时间内越过静止车辆驶离，环路随之解开。
    /// 阻挡链末端是一台长时间原地不动的运行车辆时，同样对被堵最久的车放行。
    /// </summary>
    private void BreakBumperDeadlocks()
    {
        var now = Clock.CurrentTimeUtc;

        foreach (var start in Vehicles.Values)
        {
            if (start.BumperStallSeconds < BumperStallThresholdSec) continue;
            if (start.BumperOverrideUntil.HasValue && now <= start.BumperOverrideUntil.Value) continue;

            var chain = new List<VehicleAggregate>();
            var seen = new HashSet<string>();
            VehicleAggregate? cur = start;
            while (cur != null && seen.Add(cur.Id))
            {
                chain.Add(cur);
                var nextId = BumperBlockerOf(cur);
                cur = nextId != null ? Vehicles.GetValueOrDefault(nextId) : null;
            }

            VehicleAggregate winner;
            string reason;

            if (cur != null)
            {
                // 追溯回到了链条里已有的车辆：这是一个环路
                var loop = chain.SkipWhile(c => c.Id != cur.Id).ToList();
                if (loop.Any(c => c.BumperStallSeconds < BumperStallThresholdSec)) continue;

                winner = loop
                    .OrderByDescending(c => c.AssignedOrderId != null && Orders.TryGetValue(c.AssignedOrderId, out var o) ? o.Priority : 0)
                    .ThenBy(c => c.Id, StringComparer.Ordinal)
                    .First();
                reason = $"车辆 {string.Join("、", loop.Select(c => c.Id))} 互相被防撞红线拦停超过 {BumperStallThresholdSec:F0}s，按订单优先级放行 {winner.Id}";
            }
            else
            {
                var head = chain[^1];
                if (start.BumperStallSeconds < BumperChainStallThresholdSec) continue;
                if (head.Id == start.Id || head.Status != VehicleStatus.Running || head.IsEstop) continue;

                winner = start;
                reason = $"车辆 {start.Id} 被前方静止车辆 {head.Id} 拦停超过 {BumperChainStallThresholdSec:F0}s，放行越过";
            }

            winner.BumperOverrideUntil = now.AddSeconds(BumperOverrideSec);
            winner.BumperStallSeconds = 0.0;
            DecisionLogger?.Record(DecisionCategory.TrafficAvoidance, winner.Id, "BumperOverride", "Applied", reason);
            ConflictResolved?.Invoke(this, reason);
        }
    }

    private bool CheckMutexZoneAdmittance(VehicleAggregate vehicle)
    {
        if (Graph.Zones.Count == 0) return true;

        // 如果车已经占有了互斥区，检查是否已经完全驶出
        if (!string.IsNullOrEmpty(vehicle.ClaimedMutexZoneId))
        {
            var zone = Graph.Zones.FirstOrDefault(z => z.Id == vehicle.ClaimedMutexZoneId);
            if (zone != null)
            {
                Point2D rearPt = new(
                    vehicle.Pose.X - 0.8 * Math.Cos(vehicle.Pose.ThetaRad),
                    vehicle.Pose.Y - 0.8 * Math.Sin(vehicle.Pose.ThetaRad)
                );

                bool centerInside = zone.ContainsPoint(vehicle.Pose.Position);
                bool rearInside = zone.ContainsPoint(rearPt);
                bool pathInside = vehicle.CurrentPathId != null && zone.AssociatedPathIds.Contains(vehicle.CurrentPathId);

                bool remainingTrajectoryInside = false;
                if (vehicle.TrajectoryPoints.Count > 0 && vehicle.CurrentTrajectoryIndex < vehicle.TrajectoryPoints.Count)
                {
                    int checkEnd = Math.Min(vehicle.TrajectoryPoints.Count, vehicle.CurrentTrajectoryIndex + 20);
                    for (int i = vehicle.CurrentTrajectoryIndex; i < checkEnd; i++)
                    {
                        if (zone.ContainsPoint(vehicle.TrajectoryPoints[i]))
                        {
                            remainingTrajectoryInside = true;
                            break;
                        }
                    }
                }

                bool stillInside = centerInside || rearInside || pathInside || remainingTrajectoryInside;
                if (!stillInside)
                {
                    zone.Release(vehicle.Id);
                    _zoneOccupancies.TryRemove(zone.Id, out _);
                    vehicle.ClaimedMutexZoneId = null;
                }
            }
            return true;
        }

        // 检查车辆是否正在接近某个互斥区边界 (前瞻 1~2 米或下一个路段属于互斥区)
        foreach (var zone in Graph.Zones)
        {
            bool approaching = false;
            if (vehicle.RoutePathIds.Count > 0 && vehicle.CurrentTrajectoryIndex < vehicle.TrajectoryPoints.Count)
            {
                int nextIdx = Math.Min(vehicle.TrajectoryPoints.Count - 1, vehicle.CurrentTrajectoryIndex + 15);
                var lookaheadPt = vehicle.TrajectoryPoints[nextIdx];
                if (zone.ContainsPoint(lookaheadPt) || zone.ContainsPoint(vehicle.Pose.Position))
                {
                    approaching = true;
                }
            }

            if (approaching)
            {
                if (zone.TryClaim(vehicle.Id, Clock.CurrentTimeUtc))
                {
                    vehicle.ClaimedMutexZoneId = zone.Id;
                    _zoneOccupancies[zone.Id] = vehicle.Id;
                    vehicle.BlockedByVehicleId = null;
                }
                else
                {
                    // 申请失败，在互斥区停止线前绝对停车等待放行，本拍中断物理位移推进
                    vehicle.CurrentSpeed = 0.0;
                    vehicle.BlockedByVehicleId = zone.CurrentClaimantVehicleId ?? "MUTEX_ZONE";
                    return false;
                }
            }
        }

        return true;
    }

    private void CheckStepCompletion()
    {
        foreach (var vehicle in Vehicles.Values)
        {
            if (string.IsNullOrEmpty(vehicle.AssignedOrderId)) continue;
            if (!Orders.TryGetValue(vehicle.AssignedOrderId, out var order)) continue;

            // 如果车辆当前处于让行避让状态，且已抵达避让侧线驻留等待，绝不可当作主任务到达并清空让行上下文！
            if (vehicle.IsYielding)
            {
                if (vehicle.Status == VehicleStatus.Idle && vehicle.TrajectoryPoints.Count > 0 &&
                    vehicle.CurrentTrajectoryIndex >= vehicle.TrajectoryPoints.Count)
                {
                    vehicle.CurrentNodeId = vehicle.TargetNodeId;
                    vehicle.TrajectoryPoints.Clear();
                    vehicle.CurrentTrajectoryIndex = 0;
                    if (!string.IsNullOrEmpty(vehicle.CurrentPathId))
                    {
                        ReleasePath(vehicle.CurrentPathId, vehicle.Id);
                        vehicle.CurrentPathId = null;
                    }
                }
                continue;
            }

            if (vehicle.Status == VehicleStatus.Idle && vehicle.TrajectoryPoints.Count > 0 &&
                vehicle.CurrentTrajectoryIndex >= vehicle.TrajectoryPoints.Count)
            {
                vehicle.CurrentNodeId = vehicle.TargetNodeId;
                ReleaseAllReservations(vehicle);
                vehicle.ClearRoute();

                if (order.CurrentStep?.StartedAt != null)
                {
                    double walkDuration = (Clock.CurrentTimeUtc - order.CurrentStep.StartedAt.Value).TotalSeconds;
                    order.TotalWalkTimeCostSec += Math.Max(0, walkDuration);
                }

                order.AutoRetryCount = 0;
                OrderArrivedTarget?.Invoke(this, (order, vehicle, vehicle.CurrentNodeId ?? ""));
                ExecuteNextStep(vehicle, order);
            }
        }
    }

    private void ArbitrateTraffic()
    {
        var sw = Stopwatch.StartNew();
        var running = Vehicles.Values.Where(v => v.Status == VehicleStatus.Running).ToList();
        var cycles = _deadlockDetector.DetectDeadlocks(
            running,
            ReservationManager.GetActiveEdgeOccupancies(),
            ReservationManager.GetActiveZoneOccupancies()
        );
        sw.Stop();
        BenchmarkStats.DeadlockChecksCount++;
        BenchmarkStats.DeadlockCheckDurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3);

        foreach (var cycle in cycles)
        {
            if (cycle.VehicleIds.Count >= 2)
            {
                var v1 = Vehicles.GetValueOrDefault(cycle.VehicleIds[0]);
                var v2 = Vehicles.GetValueOrDefault(cycle.VehicleIds[1]);

                if (v1 != null && v2 != null)
                {
                    // 若已有车辆在让行，不重复仲裁
                    if (v1.IsYielding || v2.IsYielding) continue;

                    var o1 = v1.AssignedOrderId != null ? Orders.GetValueOrDefault(v1.AssignedOrderId) : null;
                    var o2 = v2.AssignedOrderId != null ? Orders.GetValueOrDefault(v2.AssignedOrderId) : null;

                    var (yieldVehicleId, decision) = _avoidanceSolver.ResolveConflict(Graph, v1, v2, o1, o2);
                    ApplyAvoidanceDecision(yieldVehicleId, decision);
                    ConflictResolved?.Invoke(this, $"已裁决冲突：车辆 {yieldVehicleId} 采取策略 {decision.Strategy} - {decision.Reason}");
                }
            }
        }
    }

    private void ApplyAvoidanceDecision(string vehicleId, AvoidanceDecision decision)
    {
        if (!Vehicles.TryGetValue(vehicleId, out var vehicle)) return;
        vehicle.CurrentAvoidanceStrategy = decision.Strategy;

        switch (decision.Strategy)
        {
            case AvoidanceStrategy.WaitInPlace:
                vehicle.WaitUntilTime = Clock.CurrentTimeUtc.AddSeconds(decision.EstimatedCost);
                vehicle.CurrentSpeed = 0.0;
                break;

            case AvoidanceStrategy.PullOverSpur:
                if (decision.TargetStationId != null)
                {
                    var spurNode = Graph.GetNode(decision.TargetStationId);
                    if (spurNode != null)
                    {
                        var candidate = Graph.Nodes
                            .Where(n =>
                            {
                                double d = vehicle.Pose.Position.DistanceTo(n.Position);
                                if (d > 25.0) return false;
                                double dx = n.Position.X - vehicle.Pose.Position.X;
                                double dy = n.Position.Y - vehicle.Pose.Position.Y;
                                double fwd = Math.Cos(vehicle.Pose.ThetaRad) * dx + Math.Sin(vehicle.Pose.ThetaRad) * dy;
                                return fwd >= -0.5;
                            })
                            .OrderBy(n => vehicle.Pose.Position.DistanceTo(n.Position))
                            .FirstOrDefault();

                        string startNodeId = candidate?.Id ?? vehicle.CurrentNodeId ?? spurNode.Id;
                        var blocked = ReservationManager.GetActiveEdgeOccupancies().Where(kvp => kvp.Value != vehicle.Id).Select(kvp => kvp.Key).ToHashSet();
                        var route = _router.PlanRoute(Graph, startNodeId, decision.TargetStationId, vehicle.Kinematics, blocked);
                        if (route.IsSuccess)
                        {
                            vehicle.SuspendCurrentRouteForAvoidance(decision.TargetStationId, route.PathIds, route.SampledPoints, route.SampledIsBackward);
                        }
                        else if (decision.SampledPoints != null && decision.SampledPoints.Count > 0)
                        {
                            vehicle.SuspendCurrentRouteForAvoidance(decision.TargetStationId, decision.DetourPathIds, decision.SampledPoints, decision.SampledIsBackward);
                        }
                    }
                }
                break;

            case AvoidanceStrategy.DynamicReroute:
                if (vehicle.TargetNodeId != null && vehicle.CurrentNodeId != null)
                {
                    if (decision.SampledPoints != null && decision.SampledPoints.Count > 0)
                    {
                        vehicle.AssignRoute(vehicle.AssignedOrderId ?? "Reroute", decision.DetourPathIds, decision.SampledPoints, vehicle.TargetNodeId, decision.SampledIsBackward);
                    }
                    else
                    {
                        var blocked = ReservationManager.GetActiveEdgeOccupancies().Where(kvp => kvp.Value != vehicle.Id).Select(kvp => kvp.Key).ToHashSet();
                        var newRoute = _router.PlanRoute(Graph, vehicle.CurrentNodeId, vehicle.TargetNodeId, vehicle.Kinematics, blocked);
                        if (newRoute.IsSuccess)
                        {
                            vehicle.AssignRoute(vehicle.AssignedOrderId ?? "Reroute", newRoute.PathIds, newRoute.SampledPoints, vehicle.TargetNodeId, newRoute.SampledIsBackward);
                        }
                    }
                }
                break;
        }
    }

    private void CheckYieldingVehicleResume(VehicleAggregate vehicle)
    {
        // 避让车辆到达避让点休眠后，检测冲突路径是否已被优先车释放
        if (vehicle.IsYielding && vehicle.Status == VehicleStatus.Idle && !string.IsNullOrEmpty(vehicle.SuspendedTargetNodeId))
        {
            // 检查冲突路径段是否空出
            bool corridorClear = vehicle.SuspendedRoutePathIds.All(p => !ReservationManager.GetActiveEdgeOccupancies().TryGetValue(p, out var holder) || holder == vehicle.Id);

            if (corridorClear)
            {
                // 恢复原订单目标并重新规划继续执行
                string originalTarget = vehicle.SuspendedTargetNodeId;
                string? originalOrderId = vehicle.SuspendedOrderId;
                string startNode = vehicle.CurrentNodeId ?? Graph.FindNearestNode(vehicle.Pose.Position, 5.0)?.Id ?? originalTarget;

                var resumeRoute = _router.PlanRoute(Graph, startNode, originalTarget, vehicle.Kinematics);
                if (resumeRoute.IsSuccess)
                {
                    vehicle.IsYielding = false;
                    vehicle.CurrentAvoidanceStrategy = AvoidanceStrategy.None;
                    vehicle.AvoidanceTargetStationId = null;
                    vehicle.SuspendedOrderId = null;
                    vehicle.SuspendedTargetNodeId = null;
                    vehicle.SuspendedRoutePathIds.Clear();
                    vehicle.AssignRoute(originalOrderId ?? "ResumedTask", resumeRoute.PathIds, resumeRoute.SampledPoints, originalTarget, resumeRoute.SampledIsBackward);
                    ConflictResolved?.Invoke(this, $"车辆 {vehicle.Id} 避让完成，主干道已释放，已自动恢复原任务行驶至 {originalTarget}！");
                }
            }
        }
    }

    private void CheckAutonomousBatteryRecharge()
    {
        // 自动巡检低电量车辆
        foreach (var v in Vehicles.Values)
        {
            if (IsDispatchEligible(v) && v.Bms.SocPercent <= 20.0)
            {
                if (_lastRechargeAttempts.TryGetValue(v.Id, out var lastAttempt) &&
                    (Clock.CurrentTimeUtc - lastAttempt).TotalSeconds < 20.0)
                {
                    continue;
                }
                _lastRechargeAttempts[v.Id] = Clock.CurrentTimeUtc;

                // 搜寻就近充电桩工位
                var chargeNodes = Graph.Nodes.Where(n => n.Type == NodeType.ChargePoint).ToList();
                if (chargeNodes.Count > 0)
                {
                    var nearestCharge = chargeNodes
                        .OrderBy(n => v.Pose.DistanceTo(new Pose2D(n.Position.X, n.Position.Y, 0)))
                        .First();

                    if (v.HoldingPayload)
                    {
                        var (allocSuccess, allocReason, wipSlot) = StorageManager.AllocateSlotForDrop(StorageZoneType.Wip, $"AUTO-CHG-{v.Id}", v.Id);
                        if (allocSuccess && wipSlot != null)
                        {
                            var unloadOrder = new TransportOrderAggregate($"AUTO-CHG-{v.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", 99, v.Id)
                            {
                                TaskType = "UnloadAndCharge",
                                Target = nearestCharge.Id,
                                SourceSlotId = v.CurrentNodeId,
                                TargetSlotId = wipSlot.Id,
                                Storage = wipSlot.Id
                            };
                            unloadOrder.Steps = new List<OrderStep>
                            {
                                new()
                                {
                                    StepIndex = 0,
                                    Name = $"载货回充安全卸载：运送至缓存位 {wipSlot.Id}",
                                    Action = ActionType.Move,
                                    TargetNodeId = wipSlot.BindNodeId,
                                    TargetSlotId = wipSlot.Id
                                },
                                new()
                                {
                                    StepIndex = 1,
                                    Name = "线边暂存卸料出清",
                                    Action = ActionType.PutDown,
                                    TargetNodeId = wipSlot.BindNodeId,
                                    TargetSlotId = wipSlot.Id,
                                    CargoId = v.PayloadCarrierId ?? "CARGO",
                                    DwellSec = 3.0
                                },
                                new()
                                {
                                    StepIndex = 2,
                                    Name = "空载驶向充电机进行自动对接",
                                    Action = ActionType.Move,
                                    TargetNodeId = nearestCharge.Id
                                },
                                new()
                                {
                                    StepIndex = 3,
                                    Name = "大功率智能恒流快充至85%",
                                    Action = ActionType.Charge,
                                    TargetNodeId = nearestCharge.Id,
                                    DwellSec = 0.0
                                }
                            };
                            Orders[unloadOrder.Id] = unloadOrder;
                            OrderSubmitted?.Invoke(this, unloadOrder);
                            DecisionLogger?.Record(
                                DecisionCategory.StorageAllocation,
                                v.Id,
                                "RechargeUnloadAllocated",
                                "Success",
                                $"车辆载货低电回充，自动分配线边缓存货位 {wipSlot.Id} (站点 {wipSlot.BindNodeId}) 优先卸货出清");
                            TryDispatchOrders();
                            break;
                        }
                    }

                    var chgOrder = new TransportOrderAggregate($"AUTO-CHG-{v.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", 99, v.Id)
                    {
                        TaskType = "Charge",
                        Target = nearestCharge.Id,
                        Storage = v.CurrentNodeId ?? "PARK-01"
                    };

                    chgOrder.Steps = new List<OrderStep>
                    {
                        new()
                        {
                            StepIndex = 0,
                            Name = "驶向充电机进行自动对接",
                            Action = ActionType.Move,
                            TargetNodeId = nearestCharge.Id
                        },
                        new()
                        {
                            StepIndex = 1,
                            Name = "大功率智能恒流快充至85%",
                            Action = ActionType.Charge,
                            TargetNodeId = nearestCharge.Id,
                            DwellSec = 0.0
                        }
                    };

                    Orders[chgOrder.Id] = chgOrder;
                    OrderSubmitted?.Invoke(this, chgOrder);
                    TryDispatchOrders();
                    break;
                }
            }
        }
    }

    public void TryDispatchOrders()
    {
        var queuedOrders = Orders.Values
            .Where(o => o.Status == OrderStatus.Queued)
            .OrderByDescending(o => o.Priority)
            .ThenBy(o => o.CreatedAt)
            .ToList();

        if (queuedOrders.Count == 0) return;

        var availableVehicles = Vehicles.Values
            .Where(IsDispatchEligible)
            .ToList();

        foreach (var order in queuedOrders)
        {
            if (availableVehicles.Count == 0) break;

            VehicleAggregate? selectedVehicle = null;

            if (!string.IsNullOrEmpty(order.BindVehicleId))
            {
                selectedVehicle = availableVehicles.FirstOrDefault(v => v.Id == order.BindVehicleId);
            }
            else
            {
                var firstStep = order.CurrentStep;
                if (firstStep != null)
                {
                    var targetNode = Graph.GetNode(firstStep.TargetNodeId);
                    if (targetNode != null)
                    {
                        selectedVehicle = availableVehicles
                            .Where(v => v.Bms.SocPercent > 20.0) // 低电车辆不分派长任务
                            .OrderBy(v => v.Pose.DistanceTo(new Pose2D(targetNode.Position.X, targetNode.Position.Y, 0)))
                            .FirstOrDefault();
                    }
                }
                selectedVehicle ??= availableVehicles.FirstOrDefault(v => v.Bms.SocPercent > 20.0) ?? availableVehicles.FirstOrDefault();
            }

            if (selectedVehicle != null)
            {
                // 派单瞬间订单可能刚被取消（HTTP 线程先改了状态）：转移失败就不再占用这台车
                if (!Transition(order, OrderStatus.Assigned)) continue;
                availableVehicles.Remove(selectedVehicle);
                order.AssignedVehicleId = selectedVehicle.Id;
                selectedVehicle.AssignedOrderId = order.Id;
                order.StartedAt = Clock.CurrentTimeUtc;

                OrderStarted?.Invoke(this, (order, selectedVehicle));
                ExecuteNextStep(selectedVehicle, order);
            }
        }
    }

    private void ExecuteNextStep(VehicleAggregate vehicle, TransportOrderAggregate order)
    {
        // 终态订单不再驱动任何车辆；车辆若还挂着这张单就地释放
        if (order.IsFinished)
        {
            ReleaseVehicleFromOrder(vehicle, order);
            return;
        }

        vehicle.AssignedOrderId = order.Id;
        var step = order.CurrentStep;
        if (step == null) return;

        step.StartedAt = Clock.CurrentTimeUtc;
        if (order.Status == OrderStatus.Assigned || order.Status == OrderStatus.WaitingAction)
        {
            Transition(order, OrderStatus.Running);
        }
        // 起点站点：优先使用车辆已登记的站点；否则吸附到离车最近的站点。
        // 车辆不在路网上时（例如刚上线、被人工拖离），不能把目标站点当成起点——那会让工步在原地"瞬间到达"。
        string? startNodeId = vehicle.CurrentNodeId;
        MapNode? snapNode = null;
        if (startNodeId != null)
        {
            snapNode = Graph.GetNode(startNodeId);
            if (snapNode == null) startNodeId = null;
        }
        if (startNodeId == null)
        {
            snapNode = Graph.FindNearestNode(vehicle.Pose.Position, double.MaxValue);
            startNodeId = snapNode?.Id;
        }
        startNodeId ??= step.TargetNodeId;

        // 车辆实际位置与起点站点的偏差：超过 0.3m（小于横向偏差容限）即视为"离网"，需要先驶回路网
        bool offGraph = snapNode != null && vehicle.Pose.Position.DistanceTo(snapNode.Position) > 0.3;

        // 如果需要移动到目标节点且车辆尚未到达 (仅针对需物理到达工位的动作，控制类与信号类工步不进行前置位移)
        bool shouldNavigateFirst = step.Action switch
        {
            ActionType.WaitIoSignal => false,
            ActionType.DecisionBranch => false,
            ActionType.ClaimMutex => false,
            ActionType.ReleaseMutex => false,
            _ => !string.IsNullOrEmpty(step.TargetNodeId) && (startNodeId != step.TargetNodeId || offGraph)
        };

        // 离网车辆：目标恰好就是最近站点时，无需规划，直接直线驶入该站点
        if (shouldNavigateFirst && offGraph && startNodeId == step.TargetNodeId && snapNode != null)
        {
            vehicle.AssignRoute(order.Id, new List<string>(), new List<Point2D> { snapNode.Position }, step.TargetNodeId);
            vehicle.IsRejoining = true;
            DecisionLogger?.Record(DecisionCategory.Routing, vehicle.Id, "RejoinNetwork", "Success",
                $"车辆位于路网外 {vehicle.Pose.Position.DistanceTo(snapNode.Position):F1}m，直接驶入站点 {step.TargetNodeId}");
            return;
        }

        if (shouldNavigateFirst)
        {
            var targetHeadingRad = step.TargetHeadingDeg.HasValue ? step.TargetHeadingDeg.Value * Math.PI / 180.0 : (double?)null;
            var itinerary = _router.PlanItinerary(
                Graph,
                startNodeId,
                step.TargetNodeId,
                vehicle.Kinematics,
                targetHeadingRad: targetHeadingRad,
                isCarryingPayload: vehicle.HoldingPayload,
                plannedAtUtc: Clock.CurrentTimeUtc,
                reservationManager: ReservationManager,
                vehicleId: vehicle.Id,
                initialHeadingRad: vehicle.Pose.ThetaRad);

            if (itinerary != null)
            {
                // 时空窗整段规划时预占
                ReservationManager.ReserveItineraryTimeWindows(vehicle, order, itinerary, Clock.CurrentTimeUtc);

                vehicle.AssignRoute(
                    order.Id,
                    itinerary.AllPathIds,
                    itinerary.FlattenPoints(),
                    step.TargetNodeId,
                    itinerary.FlattenBackwardFlags(),
                    itinerary);

                if (offGraph)
                {
                    vehicle.IsRejoining = true;
                    DecisionLogger?.Record(DecisionCategory.Routing, vehicle.Id, "RejoinNetwork", "Success",
                        $"车辆位于路网外 {vehicle.Pose.Position.DistanceTo(snapNode!.Position):F1}m，先驶入站点 {startNodeId} 再沿行程前往 {step.TargetNodeId}");
                }

                if (!offGraph && itinerary.Segments.Count > 0 && itinerary.Segments[0].EstimatedEntryTimeUtc > Clock.CurrentTimeUtc.AddSeconds(0.5))
                {
                    // 迟延出发：前序走廊有预约冲突，在起始站点驻留等待时间窗开启
                    vehicle.Status = VehicleStatus.Paused;
                    vehicle.WaitUntilTime = itinerary.Segments[0].EstimatedEntryTimeUtc;
                    DecisionLogger?.Record(
                        DecisionCategory.TrafficAvoidance,
                        vehicle.Id,
                        "StationHolding",
                        "DelayedDeparture",
                        $"因走廊对向时空窗冲突，在站点 {startNodeId} 原位驻留等待至 {itinerary.Segments[0].EstimatedEntryTimeUtc:HH:mm:ss} 出发");
                }

                DecisionLogger?.Record(DecisionCategory.Routing, vehicle.Id, "PlanItinerary", "Success", $"规划结构化行程成功并预占时空窗，共 {itinerary.Segments.Count} 段，总长 {itinerary.TotalDistanceMeters:F1}m");
                return;
            }
            else
            {
                // 检查拓扑连通性：若纯几何存在路径但时空窗全时段冲突，执行站点驻留重试；若物理拓扑不通，标记失败
                var rawRoute = _router.PlanItinerary(Graph, startNodeId, step.TargetNodeId, vehicle.Kinematics, isCarryingPayload: vehicle.HoldingPayload);
                if (rawRoute != null)
                {
                    vehicle.Status = VehicleStatus.Paused;
                    vehicle.CurrentSpeed = 0.0;
                    Transition(order, OrderStatus.WaitingAction, $"走廊时空窗饱和，在站点 {startNodeId} 原位等待让行");
                    DecisionLogger?.Record(DecisionCategory.TrafficAvoidance, vehicle.Id, "StationHolding", "CorridorBlocked", $"物理拓扑可达但对向时空窗饱和，在站点 {startNodeId} 原位驻留重试");
                    long retryMs = Clock.CurrentTimestampMs + 1000;
                    ScheduleOrderEvent(order.Id, retryMs, () =>
                    {
                        if (order.Status == OrderStatus.WaitingAction && !order.IsFinished)
                        {
                            ExecuteNextStep(vehicle, order);
                        }
                    });
                    return;
                }

                var routeFailReason = $"寻路失败: 从 {startNodeId} 到 {step.TargetNodeId} 拓扑不可达";
                EndOrder(order, OrderStatus.Failed, routeFailReason);
                DecisionLogger?.Record(DecisionCategory.Routing, vehicle.Id, "PlanItinerary", "Failed", routeFailReason);
                return;
            }
        }

        // 已经位于目标工位，执行工艺动作或充电
        if (step.Action == ActionType.Charge)
        {
            if (vehicle.HoldingPayload)
            {
                // 载货充电刚性安全互锁：严禁载货直接执行充电！
                // 必须自动分配线边 WIP 缓存货位，插入前置卸货工步，若无可用缓存货位则直接熔断拦截
                var (allocOk, _, wipSlot) = StorageManager.AllocateSlotForDrop(StorageZoneType.Wip, order.Id, vehicle.Id);
                if (allocOk && wipSlot != null)
                {
                    string carrierId = vehicle.PayloadCarrierId ?? order.CargoSerial;
                    if (string.IsNullOrEmpty(carrierId)) carrierId = "CARGO-UNKNOWN";

                    DecisionLogger?.Record(
                        DecisionCategory.StorageAllocation,
                        vehicle.Id,
                        "ChargeHoldingPayloadInterlock",
                        "AutoUnload",
                        $"安全互锁：车辆载货[载具:{carrierId}]禁止直接充电，自动分配缓存货位 {wipSlot.Id} (站点 {wipSlot.BindNodeId}) 插入卸货工步序列");

                    // 构建前置卸货工步序列并插入当前订单
                    var unloadMoveStep = new OrderStep
                    {
                        StepIndex = order.CurrentStepIndex,
                        StepId = $"{step.StepId}-UNLOAD-MOVE",
                        Name = $"安全互锁: 载货充电前置驶向缓存货位 {wipSlot.Id}",
                        Action = ActionType.Move,
                        TargetNodeId = wipSlot.BindNodeId,
                        TargetSlotId = wipSlot.Id,
                        CargoId = carrierId
                    };
                    var unloadPutStep = new OrderStep
                    {
                        StepIndex = order.CurrentStepIndex + 1,
                        StepId = $"{step.StepId}-UNLOAD-PUT",
                        Name = $"安全互锁: 载货充电前置卸货至缓存货位 {wipSlot.Id}",
                        Action = ActionType.PutDown,
                        TargetNodeId = wipSlot.BindNodeId,
                        TargetSlotId = wipSlot.Id,
                        CargoId = carrierId,
                        DwellSec = 1.0
                    };

                    // 更新后续工步索引
                    order.Steps.Insert(order.CurrentStepIndex, unloadPutStep);
                    order.Steps.Insert(order.CurrentStepIndex, unloadMoveStep);
                    for (int i = 0; i < order.Steps.Count; i++)
                    {
                        order.Steps[i].StepIndex = i;
                    }

                    // 重新执行插入的第一个工步
                    ExecuteNextStep(vehicle, order);
                    return;
                }
                else
                {
                    // 无法分配缓存货位，安全互锁熔断拒绝充电
                    vehicle.Status = VehicleStatus.Paused;
                    vehicle.CurrentSpeed = 0.0;
                    const string interlockReason = "安全互锁拦截(4035): 车辆载货严禁直接充电，且当前无可用线边缓存货位进行出清卸货";
                    EndOrder(order, OrderStatus.Failed, interlockReason);
                    DecisionLogger?.Record(
                        DecisionCategory.SafetyInterlock,
                        vehicle.Id,
                        "ChargeHoldingPayloadInterlock",
                        "Rejected",
                        interlockReason);
                    return;
                }
            }

            vehicle.Status = VehicleStatus.Charging;
            OrderActionExecuting?.Invoke(this, (order, vehicle, step));
            // 真实快充闭环由 AdvanceTick 内 BMS 达到 85.0% 阈值驱动完成，绝不使用固定 20 秒提前切断
            return;
        }
        else if (step.Action == ActionType.ClaimMutex)
        {
            var zoneId = !string.IsNullOrEmpty(step.CargoId) ? step.CargoId : (!string.IsNullOrEmpty(step.OptionCode) ? step.OptionCode : "ZONE-JUNCT-C");
            var zone = Graph.Zones.FirstOrDefault(z => z.Id == zoneId || z.Name == zoneId);
            bool claimed = false;
            if (zone != null)
            {
                claimed = zone.TryClaim(vehicle.Id, Clock.CurrentTimeUtc);
                if (claimed)
                {
                    vehicle.ClaimedMutexZoneId = zone.Id;
                    _zoneOccupancies[zone.Id] = vehicle.Id;
                }
            }

            if (claimed)
            {
                AdvanceAndContinue(vehicle, order);
            }
            else
            {
                // 互斥区已被占用：绝不跨入下一工步！停在当前步等锁，防止进入冲突区
                vehicle.Status = VehicleStatus.Idle;
                vehicle.CurrentSpeed = 0.0;
                Transition(order, OrderStatus.WaitingAction, $"等待互斥区 {zoneId} 放行");
                long retryMs = Clock.CurrentTimestampMs + 500;
                ScheduleOrderEvent(order.Id, retryMs, () =>
                {
                    if (order.Status == OrderStatus.WaitingAction && !order.IsFinished)
                    {
                        ExecuteNextStep(vehicle, order);
                    }
                });
            }
        }
        else if (step.Action == ActionType.ReleaseMutex)
        {
            if (!string.IsNullOrEmpty(vehicle.ClaimedMutexZoneId))
            {
                var zone = Graph.Zones.FirstOrDefault(z => z.Id == vehicle.ClaimedMutexZoneId);
                zone?.Release(vehicle.Id);
                _zoneOccupancies.TryRemove(vehicle.ClaimedMutexZoneId, out _);
                vehicle.ClaimedMutexZoneId = null;
            }
            AdvanceAndContinue(vehicle, order);
        }
        else if (step.Action == ActionType.DecisionBranch)
        {
            Func<string, string> signalReader = signalName => _gatewaySignalReader != null ? _gatewaySignalReader(signalName) : GetIoSignal(signalName);
            bool conditionMet = Fms.Domain.Workflow.WorkflowExecutionManager.EvaluateCondition(
                vehicle,
                step.ConditionType,
                step.ConditionValue,
                signalReader,
                step.IoSignalName,
                step.CargoId);

            string? targetStepId = conditionMet ? step.TrueTargetStepId : step.FalseTargetStepId;
            int? targetStepIndex = conditionMet ? step.TrueTargetStepIndex : step.FalseTargetStepIndex;

            DecisionLogger?.Record(
                DecisionCategory.WorkflowBranch,
                vehicle.Id,
                "EvaluateBranch",
                conditionMet ? "TrueBranch" : "FalseBranch",
                $"流程决策分支 [{step.ConditionType}={step.ConditionValue}] 判定为 {conditionMet}，跳转至目标工步: {targetStepId ?? targetStepIndex?.ToString() ?? "Next"}");

            if (targetStepIndex.HasValue)
            {
                order.JumpToStep(targetStepIndex.Value, Clock.CurrentTimeUtc);
            }
            else if (!string.IsNullOrEmpty(targetStepId))
            {
                order.JumpToStepById(targetStepId, Clock.CurrentTimeUtc);
            }
            else
            {
                order.AdvanceStep(Clock.CurrentTimeUtc);
            }

            ContinueAfterAdvance(vehicle, order);
        }
        else if (step.Action == ActionType.WaitIoSignal)
        {
            string signalKey = !string.IsNullOrEmpty(step.IoSignalName) ? step.IoSignalName : "DOCK_READY";
            string currentVal = _gatewaySignalReader != null ? _gatewaySignalReader(signalKey) : GetIoSignal(signalKey);
            string expectedVal = !string.IsNullOrEmpty(step.ExpectedIoValue) ? step.ExpectedIoValue : "1";

            if (currentVal == expectedVal)
            {
                AdvanceAndContinue(vehicle, order);
            }
            else
            {
                vehicle.Status = VehicleStatus.Paused;
                Transition(order, OrderStatus.WaitingAction, $"等待 IO 信号 {signalKey} = {expectedVal}（当前 {currentVal}）");
                long retryMs = Clock.CurrentTimestampMs + 500;
                ScheduleOrderEvent(order.Id, retryMs, () =>
                {
                    if (order.Status == OrderStatus.WaitingAction && !order.IsFinished)
                    {
                        ExecuteNextStep(vehicle, order);
                    }
                });
            }
        }
        else if (step.Action == ActionType.Wait || step.Action == ActionType.LiftUp ||
                 step.Action == ActionType.PutDown || step.Action == ActionType.CheckRfid)
        {
            vehicle.Status = VehicleStatus.Paused;
            vehicle.WaitUntilTime = Clock.CurrentTimeUtc.AddSeconds(step.DwellSec);

            OrderActionExecuting?.Invoke(this, (order, vehicle, step));

            long triggerMs = Clock.CurrentTimestampMs + (long)(step.DwellSec * 1000);
            ScheduleOrderEvent(order.Id, triggerMs, () =>
            {
                order.TotalCargoActionTimeCostSec += step.DwellSec;
                vehicle.Status = VehicleStatus.Idle;

                if (step.Action == ActionType.LiftUp)
                {
                    vehicle.HoldingPayload = true;
                    var pickSlot = ResolveStorageSlot(step, order, forDrop: false);

                    // 载具编码：工步明确指定的优先；否则取货位上实际停放的载具；都没有再退回订单默认值
                    string carrierId = !string.IsNullOrEmpty(step.CargoId)
                        ? step.CargoId
                        : (!string.IsNullOrEmpty(pickSlot?.OccupiedCarrierId)
                            ? pickSlot!.OccupiedCarrierId!
                            : (!string.IsNullOrEmpty(order.CargoSerial) ? order.CargoSerial : "CARRIER-DEF"));
                    vehicle.PayloadCarrierId = carrierId;

                    if (pickSlot != null)
                    {
                        var (success, reason) = StorageManager.ExecutePick(pickSlot.Id, step.CargoId);
                        DecisionLogger?.Record(DecisionCategory.OrderDispatch, vehicle.Id, "ExecutePick", success ? "Success" : "Failed", reason);
                    }
                }
                else if (step.Action == ActionType.PutDown)
                {
                    vehicle.HoldingPayload = false;
                    string carrierId = !string.IsNullOrEmpty(vehicle.PayloadCarrierId) ? vehicle.PayloadCarrierId : (!string.IsNullOrEmpty(step.CargoId) ? step.CargoId : (!string.IsNullOrEmpty(order.CargoSerial) ? order.CargoSerial : "CARRIER-DEF"));
                    vehicle.PayloadCarrierId = null;

                    var dropSlot = ResolveStorageSlot(step, order, forDrop: true);
                    if (dropSlot != null)
                    {
                        var (success, reason) = StorageManager.ExecuteDrop(dropSlot.Id, carrierId);
                        DecisionLogger?.Record(DecisionCategory.OrderDispatch, vehicle.Id, "ExecuteDrop", success ? "Success" : "Failed", reason);
                    }
                }

                AdvanceAndContinue(vehicle, order);
            });
        }
        else
        {
            AdvanceAndContinue(vehicle, order);
        }
    }

    /// <summary>
    /// 取放货工步对应的真实货位：优先工步/订单明确指定且确实存在的货位，
    /// 其次是绑定在该工步站点上的货位；站点上没有货位时返回 null（只搬运载具，不触碰库存）。
    /// 订单里 Storage/Target 的演示占位值不是货位编号，不参与解析。
    /// </summary>
    private StorageSlot? ResolveStorageSlot(OrderStep step, TransportOrderAggregate order, bool forDrop)
    {
        foreach (var id in new[] { step.TargetSlotId, forDrop ? order.TargetSlotId : order.SourceSlotId })
        {
            if (!string.IsNullOrEmpty(id) && StorageManager.GetSlot(id) is { } explicitSlot) return explicitSlot;
        }

        if (string.IsNullOrEmpty(step.TargetNodeId)) return null;

        var atNode = StorageManager.GetAllSlots()
            .Where(slot => slot.BindNodeId == step.TargetNodeId && !slot.IsLocked)
            .OrderBy(slot => slot.Id, StringComparer.Ordinal)
            .ToList();

        if (forDrop)
        {
            return atNode.FirstOrDefault(slot => slot.Status == StorageSlotStatus.Reserved && slot.ReservedOrderId == order.Id)
                   ?? atNode.FirstOrDefault(slot => slot.Status == StorageSlotStatus.Empty);
        }

        return atNode.FirstOrDefault(slot => slot.Status == StorageSlotStatus.Reserved && slot.ReservedOrderId == order.Id && !string.IsNullOrEmpty(slot.OccupiedCarrierId))
               ?? atNode.FirstOrDefault(slot => slot.Status == StorageSlotStatus.Occupied);
    }

    private void EmitTelemetry(VehicleAggregate vehicle)
    {
        var telemetry = new VehicleTelemetry(
            vehicle.Id,
            vehicle.Pose,
            vehicle.CurrentSpeed,
            vehicle.CurrentAngularVelocity,
            vehicle.Bms.SocPercent,
            vehicle.Status,
            vehicle.CurrentPathId,
            vehicle.AssignedOrderId,
            new(),
            Clock.CurrentTimestampMs
        );
        VehicleTelemetryUpdated?.Invoke(this, telemetry);
    }

    public RouteResult PlanRoute(string startNodeId, string targetNodeId, VehicleKinematics? kinematics = null, ISet<string>? blocked = null)
    {
        var sw = Stopwatch.StartNew();
        var res = _router.PlanRoute(Graph, startNodeId, targetNodeId, kinematics, blocked);
        sw.Stop();
        BenchmarkStats.AStarCallsCount++;
        long curTicks = Interlocked.Add(ref _totalAStarDurationTicks, sw.ElapsedTicks);
        double totalMs = (double)curTicks / Stopwatch.Frequency * 1000.0;
        BenchmarkStats.AStarAvgDurationMs = Math.Round(totalMs / Math.Max(1, BenchmarkStats.AStarCallsCount), 3);
        return res;
    }

    #region 异常处理与恢复矩阵 (Fault Matrix & Self-Healing / Recovery Engine)

    public void RecordVehicleHeartbeat(string vehicleId)
    {
        if (Vehicles.TryGetValue(vehicleId, out var v))
        {
            v.LastHeartbeatUtc = Clock.CurrentTimeUtc;
            if (!v.IsOnline)
            {
                v.IsOnline = true;
                if (v.Status == VehicleStatus.Fault && !v.IsEstop)
                {
                    v.Status = VehicleStatus.Idle;
                }
            }
        }
    }

    private void CheckVehicleHeartbeats()
    {
        foreach (var v in Vehicles.Values)
        {
            if (v.IsOnline)
            {
                // 心跳超时判定 (5.0 秒无响应判定为通信掉线)
                if ((Clock.CurrentTimeUtc - v.LastHeartbeatUtc).TotalSeconds > HeartbeatTimeoutSeconds)
                {
                    v.IsOnline = false;
                    v.CurrentSpeed = 0.0;
                    if (v.Status == VehicleStatus.Running || v.Status == VehicleStatus.Paused)
                    {
                        v.Status = VehicleStatus.Fault;
                    }
                    if (!string.IsNullOrEmpty(v.AssignedOrderId) && Orders.TryGetValue(v.AssignedOrderId, out var order))
                    {
                        Transition(order, OrderStatus.WaitingAction, $"车辆 {v.Id} 通信掉线中断 (心跳超时)");
                    }
                }
            }
        }
    }

    public bool SetPathClosed(string pathId, bool isClosed, string? reason = null)
    {
        var path = Graph.GetPath(pathId);
        if (path == null) return false;
        path.IsBlocked = isClosed;

        if (isClosed)
        {
            // 扫描受该封闭路径影响的运行中车辆并执行平滑动态绕行 (Dynamic Reroute)
            foreach (var vehicle in Vehicles.Values)
            {
                if (vehicle.Status == VehicleStatus.Running && vehicle.RoutePathIds.Contains(pathId))
                {
                    TriggerDynamicReroute(vehicle, $"路径 {pathId} 临时封路管制: {reason ?? "动态安全避险"}");
                }
            }
        }
        return true;
    }

    public bool SetNodeClosed(string nodeId, bool isClosed, string? reason = null)
    {
        var node = Graph.GetNode(nodeId);
        if (node == null) return false;
        node.IsBlocked = isClosed;

        if (isClosed)
        {
            foreach (var vehicle in Vehicles.Values)
            {
                if (vehicle.Status == VehicleStatus.Running && (vehicle.TargetNodeId == nodeId || vehicle.RoutePathIds.Any(p => {
                    var path = Graph.GetPath(p);
                    return path != null && (path.FromNodeId == nodeId || path.ToNodeId == nodeId);
                })))
                {
                    TriggerDynamicReroute(vehicle, $"站点 {nodeId} 划设为动态禁行区: {reason ?? "动态禁行划设"}");
                }
            }
        }
        return true;
    }

    public bool TriggerDynamicReroute(VehicleAggregate vehicle, string reason = "动态重规划")
    {
        if (string.IsNullOrEmpty(vehicle.TargetNodeId)) return false;

        string startNode = vehicle.CurrentNodeId ?? Graph.FindNearestNode(vehicle.Pose.Position, 5.0)?.Id ?? vehicle.TargetNodeId;
        var blockedPaths = ReservationManager.GetActiveEdgeOccupancies().Where(kvp => kvp.Value != vehicle.Id).Select(kvp => kvp.Key).ToHashSet();
        foreach (var p in Graph.Paths.Where(p => p.IsBlocked))
        {
            blockedPaths.Add(p.Id);
        }

        var newRoute = PlanRoute(startNode, vehicle.TargetNodeId, vehicle.Kinematics, blockedPaths);
        if (newRoute.IsSuccess)
        {
            ReleaseAllReservations(vehicle);
            vehicle.AssignRoute(vehicle.AssignedOrderId ?? "Reroute", newRoute.PathIds, newRoute.SampledPoints, vehicle.TargetNodeId, newRoute.SampledIsBackward);
            vehicle.CurrentAvoidanceStrategy = AvoidanceStrategy.DynamicReroute;
            ConflictResolved?.Invoke(this, $"车辆 {vehicle.Id} 成功动态绕行: {reason}");
            return true;
        }
        else
        {
            // 无法绕行时，立即就近安全降速停车，置工单等待人工干预
            vehicle.CurrentSpeed = 0.0;
            vehicle.Status = VehicleStatus.Paused;
            if (!string.IsNullOrEmpty(vehicle.AssignedOrderId) && Orders.TryGetValue(vehicle.AssignedOrderId, out var order) && !order.IsFinished)
            {
                Transition(order, OrderStatus.WaitingAction, $"禁行阻断且无备选路径: {reason}");

                // 先让出占用的路权/互斥区，避免这台车把整片区域一起拖死，再自动重试
                ReleaseAllReservations(vehicle);
                ScheduleAutoRecovery(vehicle, order);
            }
            return false;
        }
    }

    private const int MaxAutoRecoveryAttempts = 15;
    private const long AutoRecoveryIntervalMs = 4000;

    /// <summary>
    /// 订单因拥堵/封路卡住时的自愈：每 4 秒重新规划一次，累计 15 次（约 1 分钟）仍无法脱困则判定失败，
    /// 由车辆释放逻辑把车和占用的资源还给调度。
    /// </summary>
    private void ScheduleAutoRecovery(VehicleAggregate vehicle, TransportOrderAggregate order)
    {
        if (order.IsFinished) return;

        if (order.AutoRetryCount >= MaxAutoRecoveryAttempts)
        {
            var giveUpReason = $"{order.FailReason}（已自动重试 {order.AutoRetryCount} 次仍无法通行，任务终止）";
            EndOrder(order, OrderStatus.Failed, giveUpReason);
            DecisionLogger?.Record(DecisionCategory.OrderDispatch, order.Id, "AutoRecovery", "GiveUp", giveUpReason);
            return;
        }

        order.AutoRetryCount++;
        ScheduleOrderEvent(order.Id, Clock.CurrentTimestampMs + AutoRecoveryIntervalMs, () =>
        {
            if (order.Status == OrderStatus.WaitingAction && !order.IsFinished && vehicle.AssignedOrderId == order.Id)
            {
                DecisionLogger?.Record(DecisionCategory.Routing, vehicle.Id, "AutoRecovery", "Retry", $"第 {order.AutoRetryCount} 次自动重新规划 {order.OrderSerial}");
                vehicle.Status = VehicleStatus.Idle;
                ExecuteNextStep(vehicle, order);
            }
        });
    }

    #endregion

    #region 多车高并发与压力基准测试 (High-Concurrency Fleet Benchmark)

    public EngineBenchmarkReport RunFleetStressBenchmark(int vehicleCount = 30, int simulatedTicks = 100)
    {
        long initialMem = GC.GetTotalMemory(true);
        var tickDurations = new List<double>(simulatedTicks);

        var landmarks = Graph.Nodes.Where(n => n.Type == NodeType.Landmark || n.Type == NodeType.PullOverPoint || n.Type == NodeType.StandbyPoint).ToList();
        if (landmarks.Count == 0) landmarks = Graph.Nodes.ToList();

        // 1. 如果现有车队数不足 vehicleCount，批量自动注入车队
        for (int i = 0; i < vehicleCount; i++)
        {
            string vId = $"BENCH-AMR-{i + 1:D2}";
            if (!Vehicles.ContainsKey(vId) && landmarks.Count > 0)
            {
                var node = landmarks[i % landmarks.Count];
                var v = new VehicleAggregate(vId, vId, new Pose2D(node.Position.X, node.Position.Y, 0))
                {
                    CurrentNodeId = node.Id,
                    Status = VehicleStatus.Idle,
                    LastHeartbeatUtc = DateTime.UtcNow
                };
                RegisterVehicle(v);
            }
        }

        // 2. 批量生成初始运输工单
        int orderIndex = 0;
        var activeBenchVehicles = Vehicles.Values.Take(vehicleCount).ToList();
        foreach (var v in activeBenchVehicles)
        {
            if (string.IsNullOrEmpty(v.AssignedOrderId) && landmarks.Count > 1)
            {
                var target = landmarks[(orderIndex * 7 + 13) % landmarks.Count];
                if (target.Id == v.CurrentNodeId) target = landmarks[(orderIndex * 7 + 14) % landmarks.Count];

                var ord = new TransportOrderAggregate($"ORD-BENCH-{orderIndex + 1:D3}", 50, v.Id);
                ord.Steps.Add(new OrderStep
                {
                    StepIndex = 0,
                    Name = $"压测转运至 {target.Id}",
                    Action = ActionType.Move,
                    TargetNodeId = target.Id
                });
                SubmitOrder(ord);
                orderIndex++;
            }
        }

        // 3. 执行指定轮数 100ms Tick 推进，高精度统计每拍耗时
        int completedOrdersCount = 0;
        EventHandler<TransportOrderAggregate> onCompleted = (s, o) => { Interlocked.Increment(ref completedOrdersCount); };
        OrderCompleted += onCompleted;

        try
        {
            for (int t = 0; t < simulatedTicks; t++)
            {
                var sw = Stopwatch.StartNew();
                AdvanceTick(0.1);
                sw.Stop();
                tickDurations.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        finally
        {
            OrderCompleted -= onCompleted;
        }

        tickDurations.Sort();
        double min = tickDurations.Count > 0 ? tickDurations[0] : 0;
        double max = tickDurations.Count > 0 ? tickDurations[^1] : 0;
        double avg = tickDurations.Count > 0 ? tickDurations.Average() : 0;
        int p95Idx = Math.Clamp((int)Math.Ceiling(tickDurations.Count * 0.95) - 1, 0, Math.Max(0, tickDurations.Count - 1));
        int p99Idx = Math.Clamp((int)Math.Ceiling(tickDurations.Count * 0.99) - 1, 0, Math.Max(0, tickDurations.Count - 1));
        double p95 = tickDurations.Count > 0 ? tickDurations[p95Idx] : 0;
        double p99 = tickDurations.Count > 0 ? tickDurations[p99Idx] : 0;

        long finalMem = GC.GetTotalMemory(false);
        long memMb = Math.Max(0, (finalMem - initialMem) / (1024 * 1024));

        bool passed = p95 < 50.0; // 工业 100ms 循环硬性实时要求：P95 < 50ms (留出 50% 余量)

        return new EngineBenchmarkReport
        {
            VehicleCount = vehicleCount,
            TotalTicksExecuted = simulatedTicks,
            MinTickDurationMs = Math.Round(min, 2),
            MaxTickDurationMs = Math.Round(max, 2),
            AvgTickDurationMs = Math.Round(avg, 2),
            P95TickDurationMs = Math.Round(p95, 2),
            P99TickDurationMs = Math.Round(p99, 2),
            AStarRoutesPlanned = BenchmarkStats.AStarCallsCount,
            AStarAvgDurationMs = BenchmarkStats.AStarAvgDurationMs,
            DeadlockChecksExecuted = BenchmarkStats.DeadlockChecksCount,
            DeadlockCheckDurationMs = BenchmarkStats.DeadlockCheckDurationMs,
            ActiveOrdersFinished = completedOrdersCount,
            MemoryAllocatedMb = memMb,
            PassedRealtimeThreshold = passed,
            Summary = $"压测规模: {vehicleCount} 台车, 运行 {simulatedTicks} 拍. 平均每拍耗时: {avg:F2}ms (P95: {p95:F2}ms, P99: {p99:F2}ms). 实时性判定: {(passed ? "优异 (达标)" : "警告 (超限)")}"
        };
    }

    #endregion
}
