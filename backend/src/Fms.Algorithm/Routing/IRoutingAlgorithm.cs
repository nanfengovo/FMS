using System.Collections.Generic;
using Fms.Domain.Topology;
using Fms.Domain.Shared.Geometry;

namespace Fms.Algorithm.Routing;

/// <summary>
/// 核心路由算法接口，支持热插拔不同的多车寻路与防死锁策略
/// </summary>
public interface IRoutingAlgorithm
{
    string AlgorithmName { get; }

    /// <summary>
    /// 为指定车辆规划从起点到终点的无冲突路径
    /// </summary>
    /// <param name="graph">全局拓扑图</param>
    /// <param name="startNodeId">起点节点ID</param>
    /// <param name="targetNodeId">终点节点ID</param>
    /// <param name="vehicleId">申请路权的车辆ID</param>
    /// <returns>路径节点序列</returns>
    List<MapNode> PlanRoute(TopologyGraph graph, string startNodeId, string targetNodeId, string vehicleId);
}
