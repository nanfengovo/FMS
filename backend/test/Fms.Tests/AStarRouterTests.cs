using Fms.Algorithm;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Xunit;

namespace Fms.Tests;

public class AStarRouterTests
{
    [Fact]
    public void PlanRoute_SimpleLinearPath_FindsOptimalPath()
    {
        var graph = new TopologyGraph();
        var nA = new MapNode("A", new Point2D(0, 0));
        var nB = new MapNode("B", new Point2D(10, 0));
        var nC = new MapNode("C", new Point2D(20, 0));

        graph.AddNode(nA); graph.AddNode(nB); graph.AddNode(nC);
        graph.AddPath(new StraightLinePath("P1", "A", "B", nA.Position, nB.Position));
        graph.AddPath(new StraightLinePath("P2", "B", "C", nB.Position, nC.Position));

        var router = new AStarRouter();
        var result = router.PlanRoute(graph, "A", "C");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.NodeIds.Count);
        Assert.Equal(new[] { "A", "B", "C" }, result.NodeIds);
        Assert.Equal(new[] { "P1", "P2" }, result.PathIds);
        Assert.Equal(20.0, result.TotalDistance, 2);
    }

    [Fact]
    public void PlanRoute_BlockedPath_ChoosesAlternativeDetour()
    {
        var graph = new TopologyGraph();
        var nStart = new MapNode("S", new Point2D(0, 0));
        var nGoal = new MapNode("G", new Point2D(20, 0));
        var nDetour = new MapNode("D", new Point2D(10, 10));

        graph.AddNode(nStart); graph.AddNode(nGoal); graph.AddNode(nDetour);

        // 主通道 (短但被阻塞)
        var pMain = new StraightLinePath("P_MAIN", "S", "G", nStart.Position, nGoal.Position);
        // 绕行通道 (长但畅通)
        var pDetour1 = new StraightLinePath("P_DETOUR_1", "S", "D", nStart.Position, nDetour.Position);
        var pDetour2 = new StraightLinePath("P_DETOUR_2", "D", "G", nDetour.Position, nGoal.Position);

        graph.AddPath(pMain);
        graph.AddPath(pDetour1);
        graph.AddPath(pDetour2);

        var router = new AStarRouter();
        var blocked = new HashSet<string> { "P_MAIN" };
        var result = router.PlanRoute(graph, "S", "G", blockedPathIds: blocked);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "S", "D", "G" }, result.NodeIds);
        Assert.Equal(new[] { "P_DETOUR_1", "P_DETOUR_2" }, result.PathIds);
    }
}
