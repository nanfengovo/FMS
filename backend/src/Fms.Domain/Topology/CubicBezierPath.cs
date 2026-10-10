using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public class CubicBezierPath : MapPath
{
    public Point2D P0 { get; set; }
    public Point2D P1 { get; set; }
    public Point2D P2 { get; set; }
    public Point2D P3 { get; set; }

    private double? _cachedLength;

    public CubicBezierPath()
    {
        Type = PathType.CubicBezier;
    }

    public CubicBezierPath(string id, string fromId, string toId, Point2D p0, Point2D p1, Point2D p2, Point2D p3, bool bidirectional = true, double maxSpeed = 1.2)
    {
        Id = id;
        FromNodeId = fromId;
        ToNodeId = toId;
        P0 = p0;
        P1 = p1;
        P2 = p2;
        P3 = p3;
        Bidirectional = bidirectional;
        MaxSpeed = maxSpeed;
        Type = PathType.CubicBezier;
    }

    public override double Length => _cachedLength ??= BezierMath.ApproximateLength(P0, P1, P2, P3, 20);

    public override Point2D Evaluate(double t) => BezierMath.EvaluateCubic(P0, P1, P2, P3, t);

    public override double GetCurvature(double t) => BezierMath.ComputeCurvature(P0, P1, P2, P3, t);

    public override List<Point2D> Sample(double stepMeters = 0.3) => BezierMath.SamplePoints(P0, P1, P2, P3, stepMeters);

    public override double CalculateCost(double vehicleMaxSpeed = 1.5)
    {
        // 曲线受到曲率影响，平均安全速度降为约 75%，并增加轻微转向惩罚权重
        double effectiveSpeed = Math.Min(MaxSpeed * 0.8, vehicleMaxSpeed * 0.8);
        double dirPenalty = Direction.Equals("backward", StringComparison.OrdinalIgnoreCase) ? 1.25 : 1.0;
        return (Length / Math.Max(0.2, effectiveSpeed) * PriorityWeight * 1.1) * dirPenalty;
    }
}
