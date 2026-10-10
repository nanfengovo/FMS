using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Orders;

public class OrderStep
{
    public int StepIndex { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ActionType Action { get; set; } = ActionType.Move;
    public string TargetNodeId { get; set; } = string.Empty;
    public string? TargetSlotId { get; set; }
    public double? TargetHeadingDeg { get; set; }
    public string OptionCode { get; set; } = "0,0"; // 对标 PDF: 机构操作码
    public string CargoId { get; set; } = string.Empty; // RFID 货物码
    public int GoalAction { get; set; } = 0; // 1-取料, 2-放料, 0-移动/其他
    public double DwellSec { get; set; } = 5.0;
    public double TimeoutSec { get; set; } = 60.0;
    public bool IsActionPermitted { get; set; } = false; // 是否已获得上位机放行 (robot_permiss_start_action)
    public bool IsRfidVerified { get; set; } = false;   // 是否已完成 RFID 校验 (check_cargo_rfid)

    // 决策分支与条件控制
    public string ConditionType { get; set; } = string.Empty; // SOC_LESS_THAN, RFID_MATCH, CARGO_LOADED, IO_SIGNAL
    public string ConditionValue { get; set; } = string.Empty;
    public string? TrueTargetStepId { get; set; }
    public string? FalseTargetStepId { get; set; }
    public int? TrueTargetStepIndex { get; set; }
    public int? FalseTargetStepIndex { get; set; }
    public string? NextStepId { get; set; }
    public bool IsTerminal { get; set; } = false;

    // IO 信号等待
    public string IoSignalName { get; set; } = string.Empty;
    public string ExpectedIoValue { get; set; } = "1";

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool IsCompleted => FinishedAt.HasValue && !IsSkipped;

    /// <summary>被操作员跳过（未真正执行）。随工步一起持久化。</summary>
    public bool IsSkipped { get; set; }
}
