using System.Collections.Concurrent;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Storage;
using Fms.Domain.Vehicles;

namespace Fms.Domain.Topology;

public record PublishRoadNetworkResult(
    bool Success,
    string? Version,
    string Message
);

/// <summary>
/// 路网生命周期管理器：管理草稿编辑态、不可变发布版本、安全发布互锁与线路视图
/// </summary>
public class RoadNetworkManager
{
    private readonly ConcurrentDictionary<string, RouteLine> _lines = new(StringComparer.OrdinalIgnoreCase);
    private int _versionCounter = 1;
    private readonly object _publishLock = new();

    public TopologyGraph DraftGraph { get; } = new() { Name = "DraftRoadNetwork" };
    public RoadNetwork? ActiveRoadNetwork { get; private set; }

    public RoadNetworkManager()
    {
        // 初始草稿与激活网络
        ActiveRoadNetwork = new RoadNetwork("RN-v1.0.0", "system", DraftGraph, new StorageManager(), null);
    }

    public void RegisterRouteLine(RouteLine line)
    {
        _lines[line.Id] = line;
    }

    public IReadOnlyList<RouteLine> GetAllRouteLines() => _lines.Values.ToList();
    public RouteLine? GetRouteLine(string lineId) => _lines.GetValueOrDefault(lineId);

    public void SyncDraftFrom(TopologyGraph sourceGraph)
    {
        lock (_publishLock)
        {
            DraftGraph.Clear();
            foreach (var n in sourceGraph.Nodes)
            {
                DraftGraph.AddNode(new MapNode(n.Id, n.Position, n.Type, n.Name)
                {
                    HeadingDeg = n.HeadingDeg,
                    AllowRevolve = n.AllowRevolve,
                    SpinDirection = n.SpinDirection,
                    ClassName = n.ClassName,
                    IsBlocked = n.IsBlocked
                });
            }

            foreach (var p in sourceGraph.Paths)
            {
                MapPath np = p is CubicBezierPath b
                    ? new CubicBezierPath(b.Id, b.FromNodeId, b.ToNodeId, b.P0, b.P1, b.P2, b.P3, b.Bidirectional, b.MaxSpeed)
                    {
                        Direction = b.Direction,
                        RoadWidth = b.RoadWidth,
                        PriorityWeight = b.PriorityWeight,
                        IsBlocked = b.IsBlocked,
                        CurveType = b.CurveType,
                        Control1 = b.Control1,
                        Control2 = b.Control2
                    }
                    : (p is StraightLinePath s
                        ? new StraightLinePath(s.Id, s.FromNodeId, s.ToNodeId, s.Start, s.End, s.Bidirectional, s.MaxSpeed)
                        {
                            Direction = s.Direction,
                            RoadWidth = s.RoadWidth,
                            PriorityWeight = s.PriorityWeight,
                            IsBlocked = s.IsBlocked,
                            CurveType = s.CurveType
                        }
                        : new StraightLinePath(p.Id, p.FromNodeId, p.ToNodeId, default, default, p.Bidirectional, p.MaxSpeed));

                np.IsBlocked = p.IsBlocked;
                foreach (var kv in p.Properties) np.Properties[kv.Key] = kv.Value;
                DraftGraph.AddPath(np);
            }

            foreach (var z in sourceGraph.Zones)
            {
                var nz = new MutexZone(z.Id, z.Name, z.MaxCapacity, z.TimeoutSec)
                {
                    Polygon = z.Polygon.ToList()
                };
                foreach (var pId in z.AssociatedPathIds) nz.AssociatedPathIds.Add(pId);
                foreach (var nId in z.AssociatedNodeIds) nz.AssociatedNodeIds.Add(nId);
                DraftGraph.AddZone(nz);
            }
        }
    }

    /// <summary>
    /// 发布全新路网版本：守住安全互锁，严禁在有在途车运行时发布破坏性拓扑更新
    /// </summary>
    public PublishRoadNetworkResult Publish(
        string publisher,
        StorageManager storageManager,
        IEnumerable<VehicleAggregate> vehicles,
        TopologyGraph? runtimeGraphToSync = null)
    {
        lock (_publishLock)
        {
            // 工业安全铁律：在途运行车辆检测
            var runningVehicles = vehicles
                .Where(v => v.IsOnline && v.Status == VehicleStatus.Running && v.RoutePathIds.Count > 0)
                .ToList();

            if (runningVehicles.Count > 0)
            {
                string busyIds = string.Join(", ", runningVehicles.Select(v => v.Id));
                return new PublishRoadNetworkResult(
                    false,
                    null,
                    $"安全互锁阻断：当前车队中存在在途移动车辆 [{busyIds}]，禁止发布路网！请等待所有车辆到达停靠点出清路权后再执行发布。");
            }

            int nextVer = ++_versionCounter;
            string newVersionTag = $"RN-v1.{nextVer / 10}.{nextVer % 10}";

            var published = new RoadNetwork(newVersionTag, publisher, DraftGraph, storageManager, _lines.Values);
            ActiveRoadNetwork = published;

            // 将冻结切片同步写入运行中调度路网，新派发的运单立即走新路网
            if (runtimeGraphToSync != null)
            {
                published.ApplyTo(runtimeGraphToSync);
            }

            return new PublishRoadNetworkResult(
                true,
                newVersionTag,
                $"路网发布成功！当前激活版本: {newVersionTag}，包含 {published.Nodes.Count} 个站点、{published.Paths.Count} 条航道、{published.Zones.Count} 个交管区。");
        }
    }
}
