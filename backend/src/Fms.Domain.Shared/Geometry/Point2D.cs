namespace Fms.Domain.Shared.Geometry;

public readonly record struct Point2D(double X, double Y)
{
    public static readonly Point2D Zero = new(0, 0);

    public double DistanceTo(Point2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double DistanceSquaredTo(Point2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public static Point2D Lerp(Point2D a, Point2D b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return new Point2D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator *(Point2D a, double scalar) => new(a.X * scalar, a.Y * scalar);
    public static Point2D operator /(Point2D a, double scalar) => new(a.X / scalar, a.Y / scalar);
}
