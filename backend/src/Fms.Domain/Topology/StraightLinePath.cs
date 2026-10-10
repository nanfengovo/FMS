using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public class StraightLinePath : MapPath
{
    public Point2D Start { get; set; }
    public Point2D End { get; set; }

    public StraightLinePath()
    {
        Type = PathType.StraightLine;
    }

    public StraightLinePath(string id, string fromId, string toId, Point2D start, Point2D end, bool bidirectional = true, double maxSpeed = 1.5)
    {
        Id = id;
        FromNodeId = fromId;
        ToNodeId = toId;
        Start = start;
        End = end;
        Bidirectional = bidirectional;
        MaxSpeed = maxSpeed;
        Type = PathType.StraightLine;
    }

    public override double Length => Start.DistanceTo(End);

    public override Point2D Evaluate(double t) => Point2D.Lerp(Start, End, t);

    public override double GetCurvature(double t) => 0.0;

    public override List<Point2D> Sample(double stepMeters = 0.3)
    {
        double len = Length;
        int steps = Math.Max(2, (int)Math.Ceiling(len / Math.Max(0.05, stepMeters)));
        var pts = new List<Point2D>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            pts.Add(Evaluate((double)i / steps));
        }
        return pts;
    }

    public override double CalculateCost(double vehicleMaxSpeed = 1.5)
    {
        double speed = Math.Min(MaxSpeed, vehicleMaxSpeed);
        double dirPenalty = Direction.Equals("backward", StringComparison.OrdinalIgnoreCase) ? 1.25 : 1.0;
        return (Length / Math.Max(0.2, speed) * PriorityWeight) * dirPenalty;
    }
}
