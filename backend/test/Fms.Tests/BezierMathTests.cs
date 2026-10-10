using Fms.Domain.Shared.Geometry;
using Xunit;

namespace Fms.Tests;

public class BezierMathTests
{
    [Fact]
    public void EvaluateCubic_EndpointsMatch()
    {
        var p0 = new Point2D(0, 0);
        var p1 = new Point2D(0, 5);
        var p2 = new Point2D(10, 5);
        var p3 = new Point2D(10, 10);

        var start = BezierMath.EvaluateCubic(p0, p1, p2, p3, 0.0);
        var end = BezierMath.EvaluateCubic(p0, p1, p2, p3, 1.0);

        Assert.Equal(p0.X, start.X, 4);
        Assert.Equal(p0.Y, start.Y, 4);
        Assert.Equal(p3.X, end.X, 4);
        Assert.Equal(p3.Y, end.Y, 4);
    }

    [Fact]
    public void ApproximateLength_IsGreaterThanEuclideanDistance()
    {
        var p0 = new Point2D(0, 0);
        var p1 = new Point2D(0, 10);
        var p2 = new Point2D(10, 10);
        var p3 = new Point2D(10, 0);

        double arcLen = BezierMath.ApproximateLength(p0, p1, p2, p3, 30);
        double chordLen = p0.DistanceTo(p3);

        Assert.True(arcLen > chordLen);
        Assert.True(arcLen > 15.0);
    }

    [Fact]
    public void Curvature_StraightLineIsZero()
    {
        // 4 控制点共线相当于直线
        var p0 = new Point2D(0, 0);
        var p1 = new Point2D(2, 0);
        var p2 = new Point2D(4, 0);
        var p3 = new Point2D(6, 0);

        double curvature = BezierMath.ComputeCurvature(p0, p1, p2, p3, 0.5);
        Assert.True(curvature < 1e-6);
    }
}
