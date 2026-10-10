using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public class MapNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public NodeType Type { get; set; } = NodeType.Landmark;
    public Point2D Position { get; set; }
    public double HeadingDeg { get; set; } = 0.0;
    public double DwellSec { get; set; } = 5.0;
    public int MaxOccupancy { get; set; } = 1;
    public string Group { get; set; } = "ALL";
    public bool AllowRevolve { get; set; } = true;
    public string SpinDirection { get; set; } = "both";
    public string ClassName { get; set; } = "LandMark";
    public bool IsBlocked { get; set; } = false;
    public Dictionary<string, string> Properties { get; set; } = new();

    public MapNode() { }

    public MapNode(string id, Point2D position, NodeType type = NodeType.Landmark, string? name = null)
    {
        Id = id;
        Position = position;
        Type = type;
        Name = name ?? id;
    }
}
