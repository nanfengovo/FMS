namespace Fms.Domain.Shared.Geometry;

public static class BezierMath
{
    public static Point2D EvaluateCubic(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;

        double x = uuu * p0.X + 3.0 * uu * t * p1.X + 3.0 * u * tt * p2.X + ttt * p3.X;
        double y = uuu * p0.Y + 3.0 * uu * t * p1.Y + 3.0 * u * tt * p2.Y + ttt * p3.Y;

        return new Point2D(x, y);
    }

    public static Point2D DerivativeCubic(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;

        double dx = 3.0 * u * u * (p1.X - p0.X) + 6.0 * u * t * (p2.X - p1.X) + 3.0 * t * t * (p3.X - p2.X);
        double dy = 3.0 * u * u * (p1.Y - p0.Y) + 6.0 * u * t * (p2.Y - p1.Y) + 3.0 * t * t * (p3.Y - p2.Y);

        return new Point2D(dx, dy);
    }

    public static Point2D SecondDerivativeCubic(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;

        double ddx = 6.0 * u * (p2.X - 2.0 * p1.X + p0.X) + 6.0 * t * (p3.X - 2.0 * p2.X + p1.X);
        double ddy = 6.0 * u * (p2.Y - 2.0 * p1.Y + p0.Y) + 6.0 * t * (p3.Y - 2.0 * p2.Y + p1.Y);

        return new Point2D(ddx, ddy);
    }

    public static double ComputeCurvature(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double t)
    {
        var d1 = DerivativeCubic(p0, p1, p2, p3, t);
        var d2 = SecondDerivativeCubic(p0, p1, p2, p3, t);

        double numerator = Math.Abs(d1.X * d2.Y - d1.Y * d2.X);
        double denominator = Math.Pow(d1.X * d1.X + d1.Y * d1.Y, 1.5);

        if (denominator < 1e-9) return 0.0;
        return numerator / denominator;
    }

    public static double ApproximateLength(Point2D p0, Point2D p1, Point2D p2, Point2D p3, int segments = 20)
    {
        double length = 0.0;
        var prev = p0;

        for (int i = 1; i <= segments; i++)
        {
            double t = (double)i / segments;
            var pt = EvaluateCubic(p0, p1, p2, p3, t);
            length += prev.DistanceTo(pt);
            prev = pt;
        }

        return length;
    }

    public static List<Point2D> SamplePoints(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double stepMeters = 0.3)
    {
        double totalLength = ApproximateLength(p0, p1, p2, p3, 25);
        int steps = Math.Max(2, (int)Math.Ceiling(totalLength / stepMeters));
        var list = new List<Point2D>(steps + 1);

        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            list.Add(EvaluateCubic(p0, p1, p2, p3, t));
        }

        return list;
    }
}
