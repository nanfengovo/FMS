namespace Fms.Domain.Workflow;

public class WorkflowTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Desc { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;
    public int Version { get; set; } = 1;
    public string VersionTag { get; set; } = "v1.0.0";
    public string ChangeLog { get; set; } = "初始创建";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<WorkflowStepItem> Steps { get; set; } = new();
}

public class WorkflowStepItem
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = "nav"; // nav, action, verify, mutex, control
    public string Name { get; set; } = string.Empty;
    public string ActionType { get; set; } = "MOVE_TO_POINT";
    public string NodeId { get; set; } = string.Empty;
    public double DwellSec { get; set; } = 1.0;
    public double? MaxSpeed { get; set; }
    public string? CargoId { get; set; }
    public string? SlotId { get; set; }
    public double? LiftStrokeMm { get; set; }
    public string? MutexZoneId { get; set; }
    public double? TimeoutSec { get; set; }
    public int? RetryCount { get; set; }

    // 条件分支与控制
    public string? ConditionType { get; set; }
    public string? ConditionValue { get; set; }
    public string? TrueTargetStepId { get; set; }
    public string? FalseTargetStepId { get; set; }
    public string? NextStepId { get; set; }
    public bool IsTerminal { get; set; } = false;

    // IO 信号等待
    public string? IoSignalName { get; set; }
    public string? ExpectedIoValue { get; set; }

    public string Status { get; set; } = "ready";
    public string? StatusDetail { get; set; }
    public string Desc { get; set; } = string.Empty;
}
