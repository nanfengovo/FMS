using Fms.Domain.Shared.Geometry;
using Fms.Domain.Vehicles;

namespace Fms.Domain.Traffic;

public record NavigationTrackingReport(
    string VehicleId,
    int CurrentSegmentIndex,
    double LateralDeviationMeters,
    double LongitudinalDistanceToGoal,
    double HeadingErrorRad,
    bool IsWithinArrivalTolerance,
    bool RequiresReroute,
    string? Message = null
);

/// <summary>
/// 车辆轨迹跟踪与导航偏差评估器：独立于搜路，监控横向偏差、到点精度与自旋航向
/// </summary>
public class NavigationTracker
{
    public double LateralToleranceMeters { get; set; } = 0.8;
    public double ArrivalDistanceToleranceMeters { get; set; } = 0.15;
    public double ArrivalHeadingToleranceRad { get; set; } = 0.15; // 约 8.6 度

    public NavigationTrackingReport EvaluateTracking(
        VehicleAggregate vehicle,
        ItinerarySegment? currentSegment,
        Point2D targetPoint,
        double? targetHeadingRad = null)
    {
        var pos = vehicle.Pose.Position;
        double distToGoal = pos.DistanceTo(targetPoint);
        double headingErr = 0.0;
        if (targetHeadingRad.HasValue)
        {
            headingErr = Math.Abs(Pose2D.NormalizeAngle(vehicle.Pose.ThetaRad - targetHeadingRad.Value));
        }

        // 计算横向偏差：车身离当前轨迹多段折线的最短垂直距离
        double lateralDev = 0.0;
        if (currentSegment != null && currentSegment.TrajectoryPoints.Count >= 2)
        {
            lateralDev = ComputeMinDistanceToPolyline(pos, currentSegment.TrajectoryPoints);
        }

        bool isArrived = distToGoal <= ArrivalDistanceToleranceMeters && headingErr <= ArrivalHeadingToleranceRad;
        bool requiresReroute = lateralDev > LateralToleranceMeters;

        return new NavigationTrackingReport(
            vehicle.Id,
            currentSegment?.SegmentIndex ?? 0,
            Math.Round(lateralDev, 3),
            Math.Round(distToGoal, 3),
            Math.Round(headingErr, 3),
            isArrived,
            requiresReroute,
            requiresReroute ? $"车辆横向偏差 {lateralDev:F2}m 超过安全阈值 {LateralToleranceMeters}m，请求调度动态重规划" : null
        );
    }

    private static double ComputeMinDistanceToPolyline(Point2D pt, IReadOnlyList<Point2D> polyline)
    {
        double minDistance = double.MaxValue;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            double d = DistancePointToSegment(pt, polyline[i], polyline[i + 1]);
            if (d < minDistance) minDistance = d;
        }
        return minDistance;
    }

    private static double DistancePointToSegment(Point2D p, Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-6) return p.DistanceTo(a);

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0.0, 1.0);
        var proj = new Point2D(a.X + t * dx, a.Y + t * dy);
        return p.DistanceTo(proj);
    }
}
