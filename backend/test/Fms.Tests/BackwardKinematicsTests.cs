using Fms.Algorithm;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class BackwardKinematicsTests
{
    [Fact]
    public void AStarRouter_ShouldIdentifyBackwardEdgeInRoute()
    {
        var graph = new TopologyGraph();
        var n1 = new MapNode("N1", new Point2D(0, 0));
        var n2 = new MapNode("N2", new Point2D(10, 0));
        graph.AddNode(n1);
        graph.AddNode(n2);

        var path = new StraightLinePath("P1", "N1", "N2", n1.Position, n2.Position, bidirectional: false)
        {
            Direction = "backward"
        };
        graph.AddPath(path);

        var router = new AStarRouter();
        var result = router.PlanRoute(graph, "N1", "N2");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SampledIsBackward);
        Assert.NotEmpty(result.SampledIsBackward);
        Assert.All(result.SampledIsBackward, isBw => Assert.True(isBw));
    }

    [Fact]
    public void VirtualAmrDynamics_ShouldOrientReverseWhenTravelingOnBackwardPath()
    {
        var vehicle = new VehicleAggregate("AMR-BW", "AMR-BW", new Pose2D(0, 0, 0));
        var trajectory = new List<Point2D>
        {
            new(2, 0),
            new(4, 0),
            new(6, 0)
        };
        var bwFlags = new List<bool> { true, true, true };

        vehicle.AssignRoute("ORD-BW", new List<string> { "P1" }, trajectory, "N2", bwFlags);
        Assert.True(vehicle.IsMovingBackward);

        var dynamics = new VirtualAmrDynamics();
        vehicle.CurrentSpeed = 1.0;
        // Step forward multiple ticks to allow smooth physical rotation to 180 deg
        for (int i = 0; i < 6; i++)
        {
            dynamics.StepSimulation(vehicle, 0.5, DateTime.UtcNow);
        }

        // Movement is along +X axis (dx=2, dy=0, tangent=0).
        // For backward edge, vehicle's heading must point in reverse direction: atan2(dy, dx) + PI = PI (+- 180 deg)
        // Check that Theta is near 180 deg, NOT near 0!
        double headingDeg = Math.Abs(vehicle.Pose.ThetaDeg);
        Assert.True(headingDeg > 170.0, $"Expected vehicle heading to reverse towards 180 degrees, but got {headingDeg} deg");
    }
}
