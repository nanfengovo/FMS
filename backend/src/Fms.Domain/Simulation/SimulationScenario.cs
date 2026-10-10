using System;
using System.Collections.Generic;

namespace Fms.Domain.Simulation;

public class SimulationScenario
{
    public string ScenarioId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Benchmark Scenario";
    public double TimeScale { get; set; } = 1.0;
    
    // Initial State
    public int VehicleSpawnCount { get; set; }
    public List<string> SpawnNodeIds { get; set; } = new();
    
    // Workload
    public int RandomOrderCount { get; set; }
    public double OrderSpawnRatePerMinute { get; set; }
    
    // Perturbations / Fault Injections
    public List<FaultInjectionRule> FaultRules { get; set; } = new();
}

public class FaultInjectionRule
{
    public string TargetVehicleId { get; set; } = string.Empty;
    public string FaultType { get; set; } = "E-Stop"; // E-Stop, CommLoss, PathBlocked
    public double TriggerAtVirtualSeconds { get; set; }
    public double DurationSeconds { get; set; }
}
