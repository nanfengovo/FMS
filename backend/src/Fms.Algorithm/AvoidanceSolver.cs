using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Topology;
using Fms.Domain.Traffic;
using Fms.Domain.Vehicles;

namespace Fms.Algorithm;

public class AvoidanceSolver
{
    private readonly AStarRouter _router;

    public AvoidanceSolver(AStarRouter router)
    {
        _router = router;
    }

    public (string yieldVehicleId, AvoidanceDecision decision) ResolveConflict(
        TopologyGraph graph,
        VehicleAggregate v1,
        VehicleAggregate v2,
        TransportOrderAggregate? order1,
        TransportOrderAggregate? order2)
    {
        // 1. 确定让行车 (Yield Vehicle)：优先级低的车让行
        int p1 = order1?.Priority ?? 50;
        int p2 = order2?.Priority ?? 50;

        VehicleAggregate yieldVehicle;
        VehicleAggregate rightOfWayVehicle;

        if (p1 > p2)
        {
            rightOfWayVehicle = v1;
            yieldVehicle = v2;
        }
        else if (p2 > p1)
        {
            rightOfWayVehicle = v2;
            yieldVehicle = v1;
        }
        else
        {
            // 优先级相同时，剩余行进距离长的车让行（快到达目的地的优先放行）
            int remaining1 = v1.TrajectoryPoints.Count - v1.CurrentTrajectoryIndex;
            int remaining2 = v2.TrajectoryPoints.Count - v2.CurrentTrajectoryIndex;

            if (remaining1 <= remaining2)
            {
                rightOfWayVehicle = v1;
                yieldVehicle = v2;
            }
            else
            {
                rightOfWayVehicle = v2;
                yieldVehicle = v1;
            }
        }

        // 搜寻在让行车行进方向前方（或当前位置最近）的拓扑节点
        var candidateNodes = graph.Nodes
            .Where(n =>
            {
                double d = yieldVehicle.Pose.Position.DistanceTo(n.Position);
                if (d > 25.0) return false;
                double dx = n.Position.X - yieldVehicle.Pose.Position.X;
                double dy = n.Position.Y - yieldVehicle.Pose.Position.Y;
                double fwd = Math.Cos(yieldVehicle.Pose.ThetaRad) * dx + Math.Sin(yieldVehicle.Pose.ThetaRad) * dy;
                return fwd >= -0.5; // 在车辆行进前方或正切位置
            })
            .OrderBy(n => yieldVehicle.Pose.Position.DistanceTo(n.Position))
            .ToList();

        var currentNearNode = candidateNodes.FirstOrDefault() ?? graph.FindNearestNode(yieldVehicle.Pose.Position, 25.0);
        var pullOverStations = graph.GetPullOverStations();

        if (currentNearNode != null && pullOverStations.Count > 0)
        {
            // 找距离让行车最近的避让点（仅避开优势车当前正在占用的路段）
            var blockedByRight = new HashSet<string>();
            if (!string.IsNullOrEmpty(rightOfWayVehicle.CurrentPathId))
            {
                blockedByRight.Add(rightOfWayVehicle.CurrentPathId);
            }

            foreach (var station in pullOverStations)
            {
                if (station.Id == yieldVehicle.CurrentNodeId || station.Id == rightOfWayVehicle.TargetNodeId)
                    continue;

                var routeToSpur = _router.PlanRoute(graph, currentNearNode.Id, station.Id, yieldVehicle.Kinematics, blockedByRight);
                if (routeToSpur.IsSuccess)
                {
                    return (
                        yieldVehicle.Id,
                        AvoidanceDecision.PullOver(station.Id, routeToSpur.PathIds, routeToSpur.TotalCost,
                            $"驶入就近避让点 {station.Name}，为优先车 {rightOfWayVehicle.Id} 让路",
                            routeToSpur.SampledPoints, routeToSpur.SampledIsBackward)
                    );
                }
            }
        }

        // 策略 B: 尝试绕行 (Dynamic Reroute)
        if (yieldVehicle.CurrentNodeId != null && yieldVehicle.TargetNodeId != null)
        {
            var blockedPaths = rightOfWayVehicle.RoutePathIds.ToHashSet();
            if (!string.IsNullOrEmpty(rightOfWayVehicle.CurrentPathId))
            {
                blockedPaths.Add(rightOfWayVehicle.CurrentPathId);
            }

            var reroute = _router.PlanRoute(graph, yieldVehicle.CurrentNodeId, yieldVehicle.TargetNodeId, yieldVehicle.Kinematics, blockedPaths);
            if (reroute.IsSuccess)
            {
                return (
                    yieldVehicle.Id,
                    AvoidanceDecision.Reroute(reroute.PathIds, reroute.SampledPoints, reroute.TotalCost,
                        $"绕开冲突路段，动态重规划新路径 (耗费 {reroute.TotalDistance:F1}m)",
                        reroute.SampledIsBackward)
                );
            }
        }

        // 策略 C: 原地等待
        return (
            yieldVehicle.Id,
            AvoidanceDecision.Wait(5.0, $"原地等待优先车 {rightOfWayVehicle.Id} 驶离冲突区域")
        );
    }
}
