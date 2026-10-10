using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;
using Fms.Domain.Simulation;
using Fms.Domain.Vehicles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fms.Engine;

public class ScenarioDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double DurationSec { get; set; } = 60.0;
    public double TimeScale { get; set; } = 1.0;
    public List<ScenarioVehicle> Vehicles { get; set; } = new();
    public List<ScenarioEvent> Events { get; set; } = new();
}

public class ScenarioVehicle
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = "LATENT";
    public string InitNodeId { get; set; } = string.Empty;
    public double MaxSpeed { get; set; } = 1.5;
}

public class ScenarioEvent
{
    public double AtSec { get; set; }
    public string Action { get; set; } = "DispatchTask";
    public string? OrderId { get; set; }
    public string? FromNodeId { get; set; }
    public string? ToNodeId { get; set; }
    public int Priority { get; set; } = 50;
    public string? TargetVehicleId { get; set; }
}

public record BenchmarkResult(
    string ScenarioId,
    int TotalOrdersSubmitted,
    int TotalOrdersCompleted,
    double DurationSec,
    double ThroughputPerHour,
    int ConflictCount,
    bool Success
);

public class ScenarioRunner
{
    public BenchmarkResult RunScenario(DispatchEngine engine, ScenarioDefinition scenario)
    {
        engine.Clock.SetTimeScale(scenario.TimeScale);
        int conflictsDetected = 0;
        engine.ConflictResolved += (_, _) => conflictsDetected++;

        // 1. 初始化车辆
        foreach (var sv in scenario.Vehicles)
        {
            var node = engine.Graph.GetNode(sv.InitNodeId);
            var initPose = node != null
                ? new Pose2D(node.Position.X, node.Position.Y, 0)
                : Pose2D.Zero;

            var v = new VehicleAggregate(sv.Id, sv.Id, initPose)
            {
                CurrentNodeId = sv.InitNodeId
            };
            v.Kinematics.MaxSpeed = sv.MaxSpeed;
            engine.RegisterVehicle(v);
        }

        // 2. 调度时间序列事件
        int submittedCount = 0;
        foreach (var evt in scenario.Events)
        {
            if (evt.Action == "DispatchTask" && !string.IsNullOrEmpty(evt.ToNodeId))
            {
                submittedCount++;
                long triggerMs = engine.Clock.CurrentTimestampMs + (long)(evt.AtSec * 1000);
                engine.Clock.ScheduleEvent(triggerMs, () =>
                {
                    var order = new TransportOrderAggregate(evt.OrderId ?? $"ORD-{Guid.NewGuid():N}", evt.Priority)
                    {
                        BindVehicleId = evt.TargetVehicleId
                    };
                    order.Steps.Add(new OrderStep
                    {
                        StepIndex = 0,
                        Action = ActionType.Move,
                        TargetNodeId = evt.ToNodeId,
                        Name = $"前往 {evt.ToNodeId}"
                    });
                    engine.SubmitOrder(order);
                });
            }
        }

        // 3. 步进执行仿真
        double dt = 0.1; // 100ms
        int totalTicks = (int)(scenario.DurationSec / dt);

        for (int i = 0; i < totalTicks; i++)
        {
            engine.AdvanceTick(dt);
        }

        int completed = engine.Orders.Values.Count(o => o.Status == OrderStatus.Done);
        double hours = scenario.DurationSec / 3600.0;
        double throughput = hours > 0 ? completed / hours : 0.0;

        return new BenchmarkResult(
            scenario.Id,
            submittedCount,
            completed,
            scenario.DurationSec,
            throughput,
            conflictsDetected,
            completed >= submittedCount
        );
    }

    public static ScenarioDefinition ParseYaml(string yamlContent)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<ScenarioDefinition>(yamlContent);
    }
}
