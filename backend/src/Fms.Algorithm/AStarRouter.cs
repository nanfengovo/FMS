using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Traffic;
using Fms.Domain.Vehicles;

namespace Fms.Algorithm;

public record RouteResult(
    bool IsSuccess,
    List<string> NodeIds,
    List<string> PathIds,
    List<Point2D> SampledPoints,
    double TotalDistance,
    double TotalCost,
    string? ErrorMessage = null,
    List<bool>? SampledIsBackward = null
)
{
    public static RouteResult Failed(string reason) =>
        new(false, new(), new(), new(), 0, double.PositiveInfinity, reason, new());
}

public class AStarRouter
{
    public readonly struct SearchState : IEquatable<SearchState>
    {
        public readonly string NodeId;
        public readonly int HeadingSector; // 0..7 (每个扇区 45 度)

        public SearchState(string nodeId, int headingSector)
        {
            NodeId = nodeId;
            HeadingSector = headingSector;
        }

        public static int AngleToSector(double angleRad)
        {
            double norm = Pose2D.NormalizeAngle(angleRad);
            if (norm < 0) norm += 2 * Math.PI;
            return ((int)Math.Round(norm / (Math.PI / 4.0))) % 8;
        }

        public static double SectorToAngle(int sector)
        {
            return (sector % 8) * (Math.PI / 4.0);
        }

        public bool Equals(SearchState other) => NodeId == other.NodeId && HeadingSector == other.HeadingSector;
        public override bool Equals(object? obj) => obj is SearchState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(NodeId, HeadingSector);
    }

    private class NodeRecord
    {
        public SearchState State { get; set; }
        public double GScore { get; set; } = double.PositiveInfinity;
        public double FScore { get; set; } = double.PositiveInfinity;
        public double HeadingRad { get; set; }
        public DateTime EstimatedTime { get; set; }
        public MapPath? PrecedingPath { get; set; }
        public SearchState? PrecedingState { get; set; }
    }

    /// <summary>
    /// 结构化时空行程规划：输出包含运动模式、建议车速、时间窗、朝向与交管区需求的 Itinerary
    /// </summary>
    public Itinerary? PlanItinerary(
        TopologyGraph graph,
        string startNodeId,
        string targetNodeId,
        VehicleKinematics? kinematics = null,
        double? targetHeadingRad = null,
        bool isCarryingPayload = false,
        DateTime? plannedAtUtc = null,
        ISet<string>? blockedPathIds = null,
        ResourceReservationManager? reservationManager = null,
        string? vehicleId = null,
        double? initialHeadingRad = null,
        bool allowDepartureDelay = true)
    {
        var startNode = graph.GetNode(startNodeId);
        var targetNode = graph.GetNode(targetNodeId);

        if (startNode == null || targetNode == null) return null;

        var plannedTime = plannedAtUtc ?? DateTime.UtcNow;

        if (startNodeId == targetNodeId)
        {
            var single = new Itinerary
            {
                StartNodeId = startNodeId,
                TargetNodeId = targetNodeId,
                TargetHeadingRad = targetHeadingRad,
                PlannedAtUtc = plannedTime,
                TotalDistanceMeters = 0.0,
                TotalDurationSec = 0.0,
                Segments = new List<ItinerarySegment>()
            };
            return single;
        }

        double maxSpeed = kinematics?.MaxSpeed ?? 1.5;
        var records = new Dictionary<SearchState, NodeRecord>();
        var openSet = new PriorityQueue<SearchState, double>();
        var closedSet = new HashSet<SearchState>();

        double startHeading = initialHeadingRad ?? (startNode.HeadingDeg * Math.PI / 180.0);
        int startSector = SearchState.AngleToSector(startHeading);
        var startState = new SearchState(startNodeId, startSector);

        var startRecord = new NodeRecord
        {
            State = startState,
            GScore = 0.0,
            FScore = Heuristic(startNode.Position, targetNode.Position, maxSpeed),
            HeadingRad = startHeading,
            EstimatedTime = plannedTime
        };
        records[startState] = startRecord;
        openSet.Enqueue(startState, startRecord.FScore);

        while (openSet.Count > 0)
        {
            SearchState currentState = openSet.Dequeue();
            if (currentState.NodeId == targetNodeId)
            {
                return BuildItinerary(graph, records, currentState, startNodeId, targetHeadingRad, maxSpeed, plannedTime);
            }

            if (!closedSet.Add(currentState)) continue;
            var currentRecord = records[currentState];

            foreach (var path in graph.GetOutgoingPaths(currentState.NodeId))
            {
                if (path.IsBlocked) continue;
                if (blockedPathIds != null && blockedPathIds.Contains(path.Id)) continue;

                string neighborId = path.FromNodeId == currentState.NodeId ? path.ToNodeId : path.FromNodeId;
                var neighborNode = graph.GetNode(neighborId);
                if (neighborNode == null || neighborNode.IsBlocked) continue;

                var pts = path.Sample(0.25);
                if (path.FromNodeId != currentState.NodeId && path.Bidirectional)
                {
                    pts.Reverse();
                }

                double edgeHeading = currentRecord.HeadingRad;
                if (pts.Count >= 2)
                {
                    edgeHeading = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X);
                }

                bool isBackward = path.Direction.Equals("backward", StringComparison.OrdinalIgnoreCase);
                double effectiveHeading = isBackward ? Pose2D.NormalizeAngle(edgeHeading + Math.PI) : edgeHeading;

                double turnDiff = Math.Abs(Pose2D.NormalizeAngle(effectiveHeading - currentRecord.HeadingRad));
                double turnCost = turnDiff * 0.5; // 自旋/转弯附加时间惩罚

                double speedLimit = Math.Min(path.MaxSpeed, maxSpeed);
                double recommendedSpeed = isBackward ? speedLimit * 0.75 : speedLimit;
                double legDuration = path.Length / Math.Max(0.3, recommendedSpeed);

                DateTime entryTime = currentRecord.EstimatedTime;
                DateTime exitTime = entryTime.AddSeconds(legDuration);

                // 核心时空窗对向冲突排查：若与其他车辆存在对向时空窗重叠，主动剪除该边
                if (reservationManager != null && !string.IsNullOrEmpty(vehicleId))
                {
                    var conflict = reservationManager.CheckTimeWindowConflict(path.Id, currentState.NodeId, neighborId, entryTime, exitTime, vehicleId);
                    if (conflict.HasConflict)
                    {
                        continue;
                    }
                }

                double stepCost = path.CalculateCost(maxSpeed) + turnCost;
                if (isCarryingPayload && isBackward)
                {
                    stepCost *= 1.15;
                }

                // 目标工位航向对齐奖励/惩罚
                if (neighborId == targetNodeId && targetHeadingRad.HasValue)
                {
                    double targetAlignDiff = Math.Abs(Pose2D.NormalizeAngle(targetHeadingRad.Value - effectiveHeading));
                    stepCost += targetAlignDiff * 0.8;
                }

                double tentativeG = currentRecord.GScore + stepCost;
                int nextSector = SearchState.AngleToSector(effectiveHeading);
                var nextState = new SearchState(neighborId, nextSector);

                if (closedSet.Contains(nextState)) continue;

                if (!records.TryGetValue(nextState, out var neighborRecord))
                {
                    neighborRecord = new NodeRecord { State = nextState };
                    records[nextState] = neighborRecord;
                }

                if (tentativeG < neighborRecord.GScore)
                {
                    neighborRecord.PrecedingState = currentState;
                    neighborRecord.PrecedingPath = path;
                    neighborRecord.GScore = tentativeG;
                    neighborRecord.HeadingRad = effectiveHeading;
                    neighborRecord.EstimatedTime = exitTime;
                    neighborRecord.FScore = tentativeG + Heuristic(neighborNode.Position, targetNode.Position, maxSpeed);

                    openSet.Enqueue(nextState, neighborRecord.FScore);
                }
            }
        }

        // 时空窗统一交通规则：若即时起步存在对向冲突，在起点站点原位等候 (Departure Delay)，测试出清后的无冲突时空窗
        if (allowDepartureDelay && reservationManager != null && !string.IsNullOrEmpty(vehicleId))
        {
            foreach (double delaySec in new[] { 3.0, 6.0, 10.0, 15.0, 20.0, 30.0 })
            {
                var delayedPlan = PlanItinerary(
                    graph,
                    startNodeId,
                    targetNodeId,
                    kinematics,
                    targetHeadingRad,
                    isCarryingPayload,
                    plannedTime.AddSeconds(delaySec),
                    blockedPathIds,
                    reservationManager,
                    vehicleId,
                    initialHeadingRad,
                    allowDepartureDelay: false);

                if (delayedPlan != null)
                {
                    return delayedPlan;
                }
            }
        }

        return null;
    }

    public RouteResult PlanRoute(
        TopologyGraph graph,
        string startNodeId,
        string targetNodeId,
        VehicleKinematics? kinematics = null,
        ISet<string>? blockedPathIds = null)
    {
        var itinerary = PlanItinerary(graph, startNodeId, targetNodeId, kinematics, null, false, null, blockedPathIds);
        if (itinerary == null)
        {
            return RouteResult.Failed($"No available path from '{startNodeId}' to '{targetNodeId}'.");
        }

        if (startNodeId == targetNodeId)
        {
            var node = graph.GetNode(startNodeId);
            return new RouteResult(true, new() { startNodeId }, new(), new() { node?.Position ?? Point2D.Zero }, 0, 0, null, new() { false });
        }

        var nodeIds = new List<string> { startNodeId };
        foreach (var s in itinerary.Segments)
        {
            nodeIds.Add(s.ToNodeId);
        }

        return new RouteResult(
            true,
            nodeIds,
            itinerary.AllPathIds,
            itinerary.FlattenPoints(),
            itinerary.TotalDistanceMeters,
            itinerary.TotalDurationSec,
            null,
            itinerary.FlattenBackwardFlags()
        );
    }

    private static double Heuristic(Point2D current, Point2D target, double maxSpeed)
    {
        return current.DistanceTo(target) / Math.Max(0.5, maxSpeed);
    }

    private static Itinerary BuildItinerary(
        TopologyGraph graph,
        Dictionary<SearchState, NodeRecord> records,
        SearchState targetState,
        string startId,
        double? targetHeadingRad,
        double maxSpeed,
        DateTime plannedTime)
    {
        var orderedNodes = new List<string>();
        var orderedPaths = new List<MapPath>();

        SearchState? curr = targetState;
        while (curr.HasValue && records.TryGetValue(curr.Value, out var rec))
        {
            orderedNodes.Insert(0, rec.State.NodeId);
            if (rec.PrecedingPath != null)
            {
                orderedPaths.Insert(0, rec.PrecedingPath);
            }
            curr = rec.PrecedingState;
        }

        var itinerary = new Itinerary
        {
            StartNodeId = startId,
            TargetNodeId = targetState.NodeId,
            TargetHeadingRad = targetHeadingRad,
            PlannedAtUtc = plannedTime
        };

        double cumulativeDistance = 0.0;
        double cumulativeDuration = 0.0;
        DateTime cursorTime = plannedTime;

        for (int i = 0; i < orderedPaths.Count; i++)
        {
            var path = orderedPaths[i];
            string fromNode = orderedNodes[i];
            string toNode = orderedNodes[i + 1];

            var pts = path.Sample(0.25);
            if (path.FromNodeId != fromNode && path.Bidirectional)
            {
                pts.Reverse();
            }

            bool isBackward = path.Direction.Equals("backward", StringComparison.OrdinalIgnoreCase);
            bool isLateral = path.CurveType.Equals("lateral", StringComparison.OrdinalIgnoreCase) ||
                             path.Properties.GetValueOrDefault("motion") == "lateral";

            var motionMode = isLateral
                ? SegmentMotionMode.LateralShift
                : isBackward
                    ? SegmentMotionMode.Backward
                    : SegmentMotionMode.Forward;

            var backwardFlags = new List<bool>(pts.Count);
            for (int p = 0; p < pts.Count; p++) backwardFlags.Add(isBackward);

            double speedLimit = Math.Min(path.MaxSpeed, maxSpeed);
            double recommendedSpeed = isBackward ? speedLimit * 0.75 : isLateral ? speedLimit * 0.6 : speedLimit;
            recommendedSpeed = Math.Max(0.3, recommendedSpeed);

            double legLength = path.Length;
            double legDurationSec = legLength / recommendedSpeed;

            // 查找从属的交管互斥区
            string? zoneId = graph.Zones.FirstOrDefault(z => z.AssociatedPathIds.Contains(path.Id))?.Id;

            double startHeading = 0.0;
            double endHeading = 0.0;
            if (pts.Count >= 2)
            {
                startHeading = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X);
                endHeading = Math.Atan2(pts[^1].Y - pts[^2].Y, pts[^1].X - pts[^2].X);
            }

            var segment = new ItinerarySegment
            {
                SegmentIndex = i,
                PathId = path.Id,
                FromNodeId = fromNode,
                ToNodeId = toNode,
                MotionMode = motionMode,
                TrajectoryPoints = pts,
                TrajectoryBackwardFlags = backwardFlags,
                RoadWidth = path.RoadWidth,
                MaxAllowedSpeed = speedLimit,
                RecommendedSpeed = recommendedSpeed,
                StartHeadingRad = startHeading,
                EndHeadingRad = endHeading,
                LengthMeters = legLength,
                EstimatedDurationSec = legDurationSec,
                EstimatedEntryTimeUtc = cursorTime,
                EstimatedExitTimeUtc = cursorTime.AddSeconds(legDurationSec),
                RequiredZoneId = zoneId
            };

            itinerary.Segments.Add(segment);

            cursorTime = cursorTime.AddSeconds(legDurationSec);
            cumulativeDistance += legLength;
            cumulativeDuration += legDurationSec;
        }

        itinerary.TotalDistanceMeters = cumulativeDistance;
        itinerary.TotalDurationSec = cumulativeDuration;

        return itinerary;
    }
}
