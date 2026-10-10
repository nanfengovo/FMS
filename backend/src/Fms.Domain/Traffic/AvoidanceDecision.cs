using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Traffic;

public record AvoidanceDecision(
    AvoidanceStrategy Strategy,
    string? TargetStationId,
    List<string> DetourPathIds,
    double EstimatedCost,
    string Reason,
    List<Point2D>? SampledPoints = null,
    List<bool>? SampledIsBackward = null
)
{
    public static AvoidanceDecision None => new(AvoidanceStrategy.None, null, new(), 0.0, "No conflict");
    public static AvoidanceDecision Wait(double waitSec, string reason) =>
        new(AvoidanceStrategy.WaitInPlace, null, new(), waitSec, reason);
    public static AvoidanceDecision PullOver(string stationId, List<string> pathToStation, double cost, string reason, List<Point2D>? sampledPoints = null, List<bool>? sampledIsBackward = null) =>
        new(AvoidanceStrategy.PullOverSpur, stationId, pathToStation, cost, reason, sampledPoints, sampledIsBackward);
    public static AvoidanceDecision Reroute(List<string> newPathIds, List<Point2D> sampledPoints, double cost, string reason, List<bool>? sampledIsBackward = null) =>
        new(AvoidanceStrategy.DynamicReroute, null, newPathIds, cost, reason, sampledPoints, sampledIsBackward);
}
