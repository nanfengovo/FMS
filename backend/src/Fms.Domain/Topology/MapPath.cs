using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public abstract class MapPath
{
    public string Id { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public PathType Type { get; protected set; }
    public bool Bidirectional { get; set; } = true;
    public double MaxSpeed { get; set; } = 1.5;
    public double PriorityWeight { get; set; } = 1.0;
    public bool IsBlocked { get; set; } = false;
    public string Direction { get; set; } = "forward";
    public double RoadWidth { get; set; } = 1.2;
    public string CurveType { get; set; } = "straight_line";
    public Point2D? Control1 { get; set; }
    public Point2D? Control2 { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<string> AllowedVehicleTypes { get; set; } = new();

    public abstract double Length { get; }
    public abstract Point2D Evaluate(double t);
    public abstract double GetCurvature(double t);
    public abstract List<Point2D> Sample(double stepMeters = 0.3);
    public abstract double CalculateCost(double vehicleMaxSpeed = 1.5);
}
