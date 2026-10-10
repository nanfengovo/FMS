using Fms.Domain.Storage;

namespace Fms.Domain.Topology;

/// <summary>
/// 不可变路网发布版本：包含点、边、交管区、库区与货位完整切片，带有全局唯一版本号
/// </summary>
public class RoadNetwork
{
    public string Version { get; set; } = string.Empty;
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
    public string PublishedBy { get; set; } = "system";

    public IReadOnlyDictionary<string, MapNode> Nodes { get; init; } = new Dictionary<string, MapNode>();
    public IReadOnlyDictionary<string, MapPath> Paths { get; init; } = new Dictionary<string, MapPath>();
    public IReadOnlyDictionary<string, MutexZone> Zones { get; init; } = new Dictionary<string, MutexZone>();
    public IReadOnlyList<StorageZone> StorageZones { get; init; } = new List<StorageZone>();
    public IReadOnlyList<StorageSlot> StorageSlots { get; init; } = new List<StorageSlot>();
    public IReadOnlyList<RouteLine> RouteLines { get; init; } = new List<RouteLine>();

    public RoadNetwork() { }

    public RoadNetwork(
        string version,
        string publisher,
        TopologyGraph graph,
        StorageManager storageManager,
        IEnumerable<RouteLine>? routeLines = null)
    {
        Version = version;
        PublishedBy = publisher;
        PublishedAtUtc = DateTime.UtcNow;

        // 深度冻结切片，杜绝引用泄露
        var clonedNodes = new Dictionary<string, MapNode>();
        foreach (var n in graph.Nodes)
        {
            clonedNodes[n.Id] = new MapNode(n.Id, n.Position, n.Type, n.Name)
            {
                HeadingDeg = n.HeadingDeg,
                AllowRevolve = n.AllowRevolve,
                SpinDirection = n.SpinDirection,
                ClassName = n.ClassName,
                IsBlocked = n.IsBlocked
            };
        }
        Nodes = clonedNodes;

        var clonedPaths = new Dictionary<string, MapPath>();
        foreach (var p in graph.Paths)
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
            clonedPaths[np.Id] = np;
        }
        Paths = clonedPaths;

        var clonedZones = new Dictionary<string, MutexZone>();
        foreach (var z in graph.Zones)
        {
            var nz = new MutexZone(z.Id, z.Name, z.MaxCapacity, z.TimeoutSec)
            {
                Polygon = z.Polygon.ToList()
            };
            foreach (var pId in z.AssociatedPathIds) nz.AssociatedPathIds.Add(pId);
            foreach (var nId in z.AssociatedNodeIds) nz.AssociatedNodeIds.Add(nId);
            clonedZones[nz.Id] = nz;
        }
        Zones = clonedZones;

        StorageZones = storageManager.GetAllZones();
        StorageSlots = storageManager.GetAllSlots();
        RouteLines = routeLines?.ToList() ?? new List<RouteLine>();
    }

    /// <summary>
    /// 将不可变发布快照同步激活至调度引擎运行时路网，确保新工单使用已发布的路网切片
    /// </summary>
    public void ApplyTo(TopologyGraph targetGraph)
    {
        targetGraph.Clear();
        foreach (var n in Nodes.Values)
        {
            targetGraph.AddNode(new MapNode(n.Id, n.Position, n.Type, n.Name)
            {
                HeadingDeg = n.HeadingDeg,
                AllowRevolve = n.AllowRevolve,
                SpinDirection = n.SpinDirection,
                ClassName = n.ClassName,
                IsBlocked = n.IsBlocked
            });
        }

        foreach (var p in Paths.Values)
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
            targetGraph.AddPath(np);
        }

        foreach (var z in Zones.Values)
        {
            var nz = new MutexZone(z.Id, z.Name, z.MaxCapacity, z.TimeoutSec)
            {
                Polygon = z.Polygon.ToList()
            };
            foreach (var pId in z.AssociatedPathIds) nz.AssociatedPathIds.Add(pId);
            foreach (var nId in z.AssociatedNodeIds) nz.AssociatedNodeIds.Add(nId);
            targetGraph.AddZone(nz);
        }
    }
}
