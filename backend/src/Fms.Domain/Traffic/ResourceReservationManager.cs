using System.Collections.Concurrent;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;

namespace Fms.Domain.Traffic;

public record LeaseClaimResult(
    bool Success,
    string? BlockedByVehicleId,
    string Reason,
    bool ShouldYieldOrWait,
    bool IsCarFollowing = false
)
{
    public static LeaseClaimResult Granted => new(true, null, "Lease granted", false);
    public static LeaseClaimResult Denied(string blockedBy, string reason, bool yield) => new(false, blockedBy, reason, yield);
    public static LeaseClaimResult Following(string leaderId) => new(true, leaderId, "Car-following approved", false, true);
}

/// <summary>
/// 集中式资源租约管理器：守住对向窄道互斥、交管区容量时间窗、同向跟驰与确定性优先级
/// </summary>
public class ResourceReservationManager
{
    private readonly ConcurrentDictionary<string, List<ResourceLease>> _edgeLeases = new();
    private readonly ConcurrentDictionary<string, List<ResourceLease>> _zoneLeases = new();
    private readonly object _syncRoot = new();

    public static double CalculatePriority(VehicleAggregate vehicle, TransportOrderAggregate? order, double remainingDistance)
    {
        double score = 100.0;
        // 规则 1: 载货且接近目标 (最高通行特权，尽快放料出清产线)
        if (vehicle.HoldingPayload && remainingDistance <= 15.0)
        {
            score += 1000.0;
        }
        // 规则 2: 低电量紧急回充
        else if (vehicle.Bms.SocPercent <= 20.0 || (order != null && order.TaskType == "Charge"))
        {
            score += 800.0;
        }
        // 规则 3: 普通载货车辆优先于空车
        else if (vehicle.HoldingPayload)
        {
            score += 500.0;
        }

        // 规则 4: 工单工位既定优先级叠加
        if (order != null)
        {
            score += order.Priority * 2.0;
        }

        // 规则 5: 同级情况下，离终点更近的车享有更高通行权
        score += Math.Max(0.0, 100.0 - Math.Min(100.0, remainingDistance));

        return score;
    }

    /// <summary>
    /// 申请行程分段所对应的边与交管区租约
    /// </summary>
    public LeaseClaimResult TryClaimSegment(
        VehicleAggregate vehicle,
        ItinerarySegment segment,
        DateTime currentUtc,
        double remainingDist,
        TransportOrderAggregate? order,
        TopologyGraph graph)
    {
        lock (_syncRoot)
        {
            double myPriority = CalculatePriority(vehicle, order, remainingDist);

            // 1. 检查几何边 (PathEdge) 冲突
            var path = graph.GetPath(segment.PathId);
            if (path != null)
            {
                if (!_edgeLeases.TryGetValue(segment.PathId, out var leases))
                {
                    leases = new List<ResourceLease>();
                    _edgeLeases[segment.PathId] = leases;
                }

                // 清理已过期的租约
                leases.RemoveAll(l => !l.IsActive || l.EndTimeUtc < currentUtc.AddSeconds(-2.0));

                var existingOtherLease = leases.FirstOrDefault(l => l.VehicleId != vehicle.Id && l.StartTimeUtc <= currentUtc.AddSeconds(1.5) && l.EndTimeUtc >= currentUtc);
                if (existingOtherLease != null)
                {
                    // 场景：两车进入同一段窄道
                    bool isOpposing = existingOtherLease.IsOpposingDirection(segment.FromNodeId, segment.ToNodeId);

                    if (isOpposing)
                    {
                        // 工业核心铁律：对向绝不可同时进入窄道！后进入车在道口外等待或改道，无论优先级高低绝不双向放行
                        return LeaseClaimResult.Denied(
                            existingOtherLease.VehicleId,
                            $"对向航道 {segment.PathId} 已被车 {existingOtherLease.VehicleId} 占用，对向绝对互斥，禁止同时进道",
                            yield: true);
                    }
                    else
                    {
                        // 同向行进：允许同向跟驰通行
                        return LeaseClaimResult.Following(existingOtherLease.VehicleId);
                    }
                }

                // 批准边租约
                var myEdgeLease = leases.FirstOrDefault(l => l.VehicleId == vehicle.Id);
                if (myEdgeLease == null)
                {
                    myEdgeLease = new ResourceLease
                    {
                        VehicleId = vehicle.Id,
                        OrderId = order?.Id ?? "",
                        ResourceType = ResourceType.PathEdge,
                        ResourceId = segment.PathId,
                        FromNodeId = segment.FromNodeId,
                        ToNodeId = segment.ToNodeId,
                        Direction = segment.IsBackward ? "backward" : "forward",
                        StartTimeUtc = currentUtc,
                        EndTimeUtc = currentUtc.AddSeconds(Math.Max(5.0, segment.EstimatedDurationSec + 3.0)),
                        PriorityScore = myPriority,
                        IsActive = true
                    };
                    leases.Add(myEdgeLease);
                }
                else
                {
                    myEdgeLease.EndTimeUtc = currentUtc.AddSeconds(Math.Max(5.0, segment.EstimatedDurationSec + 3.0));
                    myEdgeLease.PriorityScore = myPriority;
                }
            }

            // 2. 检查多边形交管区 (MutexZone) 租约
            if (!string.IsNullOrEmpty(segment.RequiredZoneId))
            {
                var zone = graph.GetZone(segment.RequiredZoneId);
                if (zone != null)
                {
                    if (!_zoneLeases.TryGetValue(zone.Id, out var zLeases))
                    {
                        zLeases = new List<ResourceLease>();
                        _zoneLeases[zone.Id] = zLeases;
                    }

                    zLeases.RemoveAll(l => !l.IsActive || l.EndTimeUtc < currentUtc.AddSeconds(-2.0));

                    // 只有与本车通行时间窗重叠的租约才占用容量：
                    // 其他车规划在更晚时刻才到达的预占窗口，不能把现在就能通过的车挡在路口外
                    var myWindowEnd = currentUtc.AddSeconds(Math.Max(10.0, segment.EstimatedDurationSec + 5.0));
                    var activeOthers = zLeases
                        .Where(l => l.VehicleId != vehicle.Id && l.StartTimeUtc <= myWindowEnd && l.EndTimeUtc >= currentUtc)
                        .ToList();
                    if (activeOthers.Count >= zone.MaxCapacity)
                    {
                        // 容量已满，必须在交管区入口外停靠等待
                        var primaryBlocker = activeOthers.OrderByDescending(o => o.PriorityScore).First();
                        return LeaseClaimResult.Denied(
                            primaryBlocker.VehicleId,
                            $"交管区 {zone.Name} ({zone.Id}) 容量达到上限 {zone.MaxCapacity}，等待在途车出清",
                            yield: true);
                    }

                    // 批准交管区租约
                    var myZoneLease = zLeases.FirstOrDefault(l => l.VehicleId == vehicle.Id);
                    if (myZoneLease == null)
                    {
                        myZoneLease = new ResourceLease
                        {
                            VehicleId = vehicle.Id,
                            OrderId = order?.Id ?? "",
                            ResourceType = ResourceType.MutexZone,
                            ResourceId = zone.Id,
                            StartTimeUtc = currentUtc,
                            EndTimeUtc = currentUtc.AddSeconds(Math.Max(10.0, segment.EstimatedDurationSec + 5.0)),
                            PriorityScore = myPriority,
                            IsActive = true
                        };
                        zLeases.Add(myZoneLease);
                    }
                }
            }

            return LeaseClaimResult.Granted;
        }
    }

    public void ReleaseSegment(string vehicleId, string pathId)
    {
        lock (_syncRoot)
        {
            if (_edgeLeases.TryGetValue(pathId, out var leases))
            {
                leases.RemoveAll(l => l.VehicleId == vehicleId);
            }
        }
    }

    public void ReleaseZone(string vehicleId, string zoneId)
    {
        lock (_syncRoot)
        {
            if (_zoneLeases.TryGetValue(zoneId, out var leases))
            {
                leases.RemoveAll(l => l.VehicleId == vehicleId);
            }
        }
    }

    public void ReleaseAll(string vehicleId)
    {
        lock (_syncRoot)
        {
            foreach (var list in _edgeLeases.Values)
            {
                list.RemoveAll(l => l.VehicleId == vehicleId);
            }
            foreach (var list in _zoneLeases.Values)
            {
                list.RemoveAll(l => l.VehicleId == vehicleId);
            }
        }
    }

    /// <summary>
    /// 检查指定时间窗内在边上是否存在对向通行冲突 (时空窗冲突检测)
    /// </summary>
    public (bool HasConflict, string? ConflictingVehicleId, string Reason) CheckTimeWindowConflict(
        string pathId,
        string fromNodeId,
        string toNodeId,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        string vehicleId)
    {
        lock (_syncRoot)
        {
            if (!_edgeLeases.TryGetValue(pathId, out var leases))
            {
                return (false, null, "No leases on path");
            }

            foreach (var lease in leases)
            {
                if (!lease.IsActive || lease.VehicleId == vehicleId) continue;

                // 判断对向
                if (lease.IsOpposingDirection(fromNodeId, toNodeId))
                {
                    // 检查对向时空窗重叠: 两个时空区间存在相交
                    if (startTimeUtc < lease.EndTimeUtc && lease.StartTimeUtc < endTimeUtc)
                    {
                        return (true, lease.VehicleId, $"与对向车辆 {lease.VehicleId} 在航道 {pathId} 存在时空窗重叠冲突 ([{startTimeUtc:HH:mm:ss}-{endTimeUtc:HH:mm:ss}] vs [{lease.StartTimeUtc:HH:mm:ss}-{lease.EndTimeUtc:HH:mm:ss}])");
                    }
                }
            }

            return (false, null, "No opposing conflict");
        }
    }

    /// <summary>
    /// 规划时整段时空窗预占：将行程各分段的时间窗提前锁入租约管理器，防止对向车辆侵入
    /// </summary>
    public void ReserveItineraryTimeWindows(
        VehicleAggregate vehicle,
        TransportOrderAggregate? order,
        Itinerary itinerary,
        DateTime currentUtc)
    {
        lock (_syncRoot)
        {
            ReleaseAll(vehicle.Id);

            double priority = CalculatePriority(vehicle, order, itinerary.TotalDistanceMeters);

            foreach (var seg in itinerary.Segments)
            {
                if (!_edgeLeases.TryGetValue(seg.PathId, out var leases))
                {
                    leases = new List<ResourceLease>();
                    _edgeLeases[seg.PathId] = leases;
                }

                var edgeLease = new ResourceLease
                {
                    VehicleId = vehicle.Id,
                    OrderId = order?.Id ?? "",
                    ResourceType = ResourceType.PathEdge,
                    ResourceId = seg.PathId,
                    FromNodeId = seg.FromNodeId,
                    ToNodeId = seg.ToNodeId,
                    Direction = seg.IsBackward ? "backward" : "forward",
                    StartTimeUtc = seg.EstimatedEntryTimeUtc,
                    EndTimeUtc = seg.EstimatedExitTimeUtc.AddSeconds(2.0),
                    PriorityScore = priority,
                    IsActive = true
                };
                leases.Add(edgeLease);

                if (!string.IsNullOrEmpty(seg.RequiredZoneId))
                {
                    if (!_zoneLeases.TryGetValue(seg.RequiredZoneId, out var zLeases))
                    {
                        zLeases = new List<ResourceLease>();
                        _zoneLeases[seg.RequiredZoneId] = zLeases;
                    }

                    var zoneLease = new ResourceLease
                    {
                        VehicleId = vehicle.Id,
                        OrderId = order?.Id ?? "",
                        ResourceType = ResourceType.MutexZone,
                        ResourceId = seg.RequiredZoneId,
                        StartTimeUtc = seg.EstimatedEntryTimeUtc,
                        EndTimeUtc = seg.EstimatedExitTimeUtc.AddSeconds(3.0),
                        PriorityScore = priority,
                        IsActive = true
                    };
                    zLeases.Add(zoneLease);
                }
            }
        }
    }

    public Dictionary<string, string> GetActiveEdgeOccupancies()
    {
        lock (_syncRoot)
        {
            var dict = new Dictionary<string, string>();
            foreach (var kvp in _edgeLeases)
            {
                var active = kvp.Value.FirstOrDefault(l => l.IsActive);
                if (active != null)
                {
                    dict[kvp.Key] = active.VehicleId;
                }
            }
            return dict;
        }
    }

    public Dictionary<string, string> GetActiveZoneOccupancies()
    {
        lock (_syncRoot)
        {
            var dict = new Dictionary<string, string>();
            foreach (var kvp in _zoneLeases)
            {
                var active = kvp.Value.FirstOrDefault(l => l.IsActive);
                if (active != null)
                {
                    dict[kvp.Key] = active.VehicleId;
                }
            }
            return dict;
        }
    }

    public IReadOnlyList<ResourceLease> GetActiveLeases()
    {
        lock (_syncRoot)
        {
            var result = new List<ResourceLease>();
            foreach (var list in _edgeLeases.Values) result.AddRange(list);
            foreach (var list in _zoneLeases.Values) result.AddRange(list);
            return result;
        }
    }
}
