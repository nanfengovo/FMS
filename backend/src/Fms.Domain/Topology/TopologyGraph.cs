using System.Collections.Concurrent;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public class TopologyGraph
{
    public string Name { get; set; } = "DefaultMap";
    public int Version { get; set; } = 1;
    public double MinX { get; set; } = 0;
    public double MaxX { get; set; } = 100;
    public double MinY { get; set; } = 0;
    public double MaxY { get; set; } = 100;

    private readonly ConcurrentDictionary<string, MapNode> _nodes = new();
    private readonly ConcurrentDictionary<string, MapPath> _paths = new();
    private readonly ConcurrentDictionary<string, MutexZone> _zones = new();

    // 邻接表：NodeId -> Outgoing Paths
    private readonly ConcurrentDictionary<string, List<MapPath>> _adjacency = new();

    public IReadOnlyCollection<MapNode> Nodes => _nodes.Values.ToArray();
    public IReadOnlyCollection<MapPath> Paths => _paths.Values.ToArray();
    public IReadOnlyCollection<MutexZone> Zones => _zones.Values.ToArray();

    public void AddNode(MapNode node)
    {
        _nodes[node.Id] = node;
        _adjacency.TryAdd(node.Id, new List<MapPath>());
    }

    public void AddPath(MapPath path)
    {
        _paths[path.Id] = path;

        // 更新出度邻接
        var listFrom = _adjacency.GetOrAdd(path.FromNodeId, _ => new List<MapPath>());
        lock (listFrom)
        {
            if (!listFrom.Any(p => p.Id == path.Id)) listFrom.Add(path);
        }

        if (path.Bidirectional)
        {
            var listTo = _adjacency.GetOrAdd(path.ToNodeId, _ => new List<MapPath>());
            lock (listTo)
            {
                if (!listTo.Any(p => p.Id == path.Id)) listTo.Add(path);
            }
        }
    }

    public void AddZone(MutexZone zone)
    {
        _zones[zone.Id] = zone;
    }

    public MapNode? GetNode(string id) => _nodes.GetValueOrDefault(id);
    public MapPath? GetPath(string id) => _paths.GetValueOrDefault(id);
    public MutexZone? GetZone(string id) => _zones.GetValueOrDefault(id);

    public bool RemoveNode(string id)
    {
        _adjacency.TryRemove(id, out _);
        return _nodes.TryRemove(id, out _);
    }

    public bool RemovePath(string id) => _paths.TryRemove(id, out _);
    public bool RemoveZone(string id) => _zones.TryRemove(id, out _);

    public IReadOnlyList<MapPath> GetOutgoingPaths(string nodeId)
    {
        if (_adjacency.TryGetValue(nodeId, out var paths))
        {
            lock (paths)
            {
                return paths.ToList();
            }
        }
        return Array.Empty<MapPath>();
    }

    public MapNode? FindNearestNode(Point2D pt, double maxDistance = 5.0)
    {
        MapNode? best = null;
        double bestDist = double.MaxValue;

        foreach (var node in _nodes.Values)
        {
            double d = node.Position.DistanceTo(pt);
            if (d < bestDist && d <= maxDistance)
            {
                bestDist = d;
                best = node;
            }
        }

        return best;
    }

    public List<MapNode> GetPullOverStations()
    {
        return _nodes.Values
            .Where(n => n.Type == NodeType.PullOverPoint || n.Type == NodeType.StandbyPoint || n.Type == NodeType.ChargePoint)
            .ToList();
    }

    public void Clear()
    {
        _nodes.Clear();
        _paths.Clear();
        _zones.Clear();
        _adjacency.Clear();
    }
}
