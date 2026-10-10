namespace Fms.Domain.Shared.Geometry;

public readonly record struct Pose2D(double X, double Y, double ThetaRad)
{
    public static readonly Pose2D Zero = new(0, 0, 0);

    public Point2D Position => new(X, Y);

    public double ThetaDeg => ThetaRad * 180.0 / Math.PI;

    public static Pose2D FromDegrees(double x, double y, double deg)
    {
        return new Pose2D(x, y, deg * Math.PI / 180.0);
    }

    public double DistanceTo(Pose2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double NormalizeAngle(double angleRad)
    {
        while (angleRad > Math.PI) angleRad -= 2.0 * Math.PI;
        while (angleRad < -Math.PI) angleRad += 2.0 * Math.PI;
        return angleRad;
    }
}
