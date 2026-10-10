using Fms.Domain.Vehicles;

namespace Fms.Algorithm;

public record DeadlockCycle(List<string> VehicleIds, string Description);

public class DeadlockDetector
{
    public List<DeadlockCycle> DetectDeadlocks(
        IReadOnlyCollection<VehicleAggregate> vehicles,
        Dictionary<string, string> pathOccupancies, // PathId -> CurrentOccupantVehicleId
        Dictionary<string, string> zoneOccupancies) // ZoneId -> CurrentOccupantVehicleId
    {
        var waitGraph = new Dictionary<string, List<string>>(); // VehicleId -> List of vehicles it is waiting for
        var cycles = new List<DeadlockCycle>();

        // 1. 构建等待图 (Wait-For Graph)
        foreach (var v in vehicles)
        {
            if (!v.IsOnline || v.IsEstop) continue;
            waitGraph[v.Id] = new List<string>();

            // 如果当前车辆因资源互斥等待其他车辆
            if (!string.IsNullOrEmpty(v.BlockedByVehicleId))
            {
                waitGraph[v.Id].Add(v.BlockedByVehicleId);
            }
        }

        // 2. 检测严格的对向对冲冲突 (Head-on Conflict on Opposing Directions)
        // 排除：已在避让中的车、同向跟车的车、已经停靠让行的车
        var activeVehicles = vehicles
            .Where(v => v.Status == Domain.Shared.Enums.VehicleStatus.Running && !v.IsYielding && v.CurrentAvoidanceStrategy == Domain.Shared.Enums.AvoidanceStrategy.None)
            .ToList();

        for (int i = 0; i < activeVehicles.Count; i++)
        {
            var v1 = activeVehicles[i];
            for (int j = i + 1; j < activeVehicles.Count; j++)
            {
                var v2 = activeVehicles[j];

                // 1. 距离粗筛：空间间距超过 25m 的车体绝不发生即刻迎面对冲，无需耗费开销比对路径集合 (O(1) 过滤 98% 远距车对)
                double dx = v2.Pose.Position.X - v1.Pose.Position.X;
                double dy = v2.Pose.Position.Y - v1.Pose.Position.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq > 625.0) continue; // 25m^2

                // 2. 运动矢量与朝向分析：计算方向向量点积
                double v1Dx = Math.Cos(v1.Pose.ThetaRad);
                double v1Dy = Math.Sin(v1.Pose.ThetaRad);
                double v2Dx = Math.Cos(v2.Pose.ThetaRad);
                double v2Dy = Math.Sin(v2.Pose.ThetaRad);
                double headingDot = v1Dx * v2Dx + v1Dy * v2Dy;

                // 如果 headingDot > -0.3，说明两车大致同向（同向跟车走廊），留给跟驰模型调速，不属于对冲死锁
                if (headingDot > -0.3) continue;

                // 3. 相对位移投影，确认两车是否相向迎面驶近
                double approachV1 = v1Dx * dx + v1Dy * dy;
                if (approachV1 <= 0) continue;

                // 4. 精确路径交汇重叠比对 (仅针对近距离迎面驶近的车对执行)
                bool sharesRoute = (!string.IsNullOrEmpty(v1.CurrentPathId) && v1.CurrentPathId == v2.CurrentPathId);
                if (!sharesRoute && v1.RoutePathIds.Count > 0 && v2.RoutePathIds.Count > 0)
                {
                    if (v1.RoutePathIds.Count <= v2.RoutePathIds.Count)
                    {
                        for (int k = 0; k < v1.RoutePathIds.Count; k++)
                        {
                            if (v2.RoutePathIds.Contains(v1.RoutePathIds[k])) { sharesRoute = true; break; }
                        }
                    }
                    else
                    {
                        for (int k = 0; k < v2.RoutePathIds.Count; k++)
                        {
                            if (v1.RoutePathIds.Contains(v2.RoutePathIds[k])) { sharesRoute = true; break; }
                        }
                    }
                }

                if (!sharesRoute) continue;

                double distance = Math.Sqrt(distSq);
                cycles.Add(new DeadlockCycle(
                    new() { v1.Id, v2.Id },
                    $"狭窄廊道对向相向对冲冲突: 车辆 {v1.Id} 与 {v2.Id} 迎面逼近 (间距: {distance:F2}m, 夹角点积: {headingDot:F2})"
                ));
            }
        }

        // 3. DFS 环路检测 (Wait-For Graph Cycle Detection)
        var visited = new HashSet<string>();
        var recStack = new HashSet<string>();
        var currentPath = new List<string>();

        foreach (var vId in waitGraph.Keys)
        {
            if (FindCycleDfs(vId, waitGraph, visited, recStack, currentPath, out var cycle))
            {
                if (!cycles.Any(c => c.VehicleIds.ToHashSet().SetEquals(cycle)))
                {
                    cycles.Add(new DeadlockCycle(cycle, $"检测到等待图闭环死锁: {string.Join(" -> ", cycle)}"));
                }
            }
        }

        return cycles;
    }

    private static bool FindCycleDfs(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recStack,
        List<string> currentPath,
        out List<string> cycleNodes)
    {
        cycleNodes = new List<string>();
        if (recStack.Contains(node))
        {
            int idx = currentPath.IndexOf(node);
            if (idx >= 0)
            {
                cycleNodes = currentPath.Skip(idx).ToList();
                cycleNodes.Add(node);
                return true;
            }
        }

        if (visited.Contains(node)) return false;

        visited.Add(node);
        recStack.Add(node);
        currentPath.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (FindCycleDfs(neighbor, graph, visited, recStack, currentPath, out cycleNodes))
                {
                    return true;
                }
            }
        }

        recStack.Remove(node);
        currentPath.RemoveAt(currentPath.Count - 1);
        return false;
    }
}
