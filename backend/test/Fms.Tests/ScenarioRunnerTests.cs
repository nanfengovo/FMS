using Fms.Domain.Shared.Geometry;
using Fms.Domain.Topology;
using Fms.Engine;
using Xunit;

namespace Fms.Tests;

public class ScenarioRunnerTests
{
    private const string SampleScenarioYaml = @"
id: 'NXP-Cleanroom-Test-01'
name: '洁净室自动化搬运场景'
durationSec: 15.0
timeScale: 2.0
vehicles:
  - id: 'AMR-01'
    initNodeId: 'STK'
    maxSpeed: 2.0
events:
  - atSec: 0.5
    action: 'DispatchTask'
    orderId: 'TASK-101'
    toNodeId: 'WB'
    priority: 80
";

    [Fact]
    public void ScenarioRunner_RunsYamlScenario_CompletesSuccessfully()
    {
        var engine = new DispatchEngine();
        var nStk = new MapNode("STK", new Point2D(0, 0), Domain.Shared.Enums.NodeType.JobPoint);
        var nWb = new MapNode("WB", new Point2D(20, 0), Domain.Shared.Enums.NodeType.JobPoint);

        engine.Graph.AddNode(nStk);
        engine.Graph.AddNode(nWb);
        engine.Graph.AddPath(new StraightLinePath("P1", "STK", "WB", nStk.Position, nWb.Position));

        var scenario = ScenarioRunner.ParseYaml(SampleScenarioYaml);
        var runner = new ScenarioRunner();

        var result = runner.RunScenario(engine, scenario);

        Assert.Equal("NXP-Cleanroom-Test-01", result.ScenarioId);
        Assert.Equal(1, result.TotalOrdersSubmitted);
        Assert.Equal(1, result.TotalOrdersCompleted);
        Assert.True(result.Success);
    }
}
