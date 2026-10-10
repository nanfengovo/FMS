using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Workflow;

public class WorkflowNodeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ActionType { get; set; } = "MOVE_TO_POINT"; // MOVE_TO_POINT, PICK_CARGO, DROP_CARGO, CHARGE, WAIT_IO_SIGNAL, DECISION_BRANCH, TERMINAL
    public string TargetNodeId { get; set; } = string.Empty;
    public string TargetSlotId { get; set; } = string.Empty;
    public double DwellSec { get; set; } = 1.0;
    public string? CargoId { get; set; }

    // IO 信号等待
    public string? IoSignalName { get; set; }
    public string? ExpectedIoValue { get; set; } = "1";

    // 条件分支
    public string? ConditionType { get; set; } // SOC_LESS_THAN, RFID_MATCH, CARGO_LOADED, IO_SIGNAL
    public string? ConditionValue { get; set; }
    public string? TrueTargetNodeId { get; set; }
    public string? FalseTargetNodeId { get; set; }

    public string? NextNodeId { get; set; }
    public bool IsTerminal { get; set; } = false;
}

public class WorkflowDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string VersionTag { get; set; } = "v1.0.0";
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<WorkflowNodeDefinition> Nodes { get; set; } = new();

    public WorkflowNodeDefinition? GetNode(string nodeId) => Nodes.FirstOrDefault(n => n.Id == nodeId);
}
