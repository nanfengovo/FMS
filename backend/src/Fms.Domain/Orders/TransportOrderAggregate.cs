using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Orders;

/// <summary>
/// 运输订单聚合。订单状态只能通过 <see cref="TryTransition"/> 改变（合法转移见 <see cref="OrderStateMachine"/>），
/// 工步状态由订单状态与当前工步下标推导（<see cref="GetStepStatus"/>），不单独存储。
/// 订单进入终态（已完成 / 已失败 / 已取消）后，所有修改方法都不再生效。
/// </summary>
public class TransportOrderAggregate
{
    private readonly object _gate = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderSerial { get; set; } = string.Empty; // task_serial (外部编号)
    public string BulkTaskType { get; set; } = "task";
    public string TaskType { get; set; } = "Transport";
    public int Priority { get; set; } = 50;
    public int Succession { get; set; } = 0; // 接续号
    public string PreReport { get; set; } = "0";

    public string? BindVehicleId { get; set; } // 指定车辆 ID，0 为不指定
    public string? AssignedVehicleId { get; set; } // 实际分配车辆 ID（订单结束后保留为历史执行车辆）
    public string RobotType { get; set; } = "99";
    public List<string> AreaProperty { get; set; } = new();

    /// <summary>订单状态。只读；变更必须走 <see cref="TryTransition"/>，重启恢复走 <see cref="RestoreStatus"/>。</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Queued;
    public List<OrderStep> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; } = 0;

    // 接口协议扩展属性
    public string SubNo { get; set; } = string.Empty;
    public string TaskCodeA { get; set; } = "133121";
    public string TaskCodeB { get; set; } = "133635";
    public string FinishingAction { get; set; } = "Parking or charging";
    public string CargoSerial { get; set; } = "1";
    public int ArmAction { get; set; } = 1;
    public string Rfid { get; set; } = "#";
    public int Mark { get; set; } = 3;
    public string OptionCode { get; set; } = "131073,133635";
    public string PassPoint { get; set; } = "1";
    public string PassPointToGoal { get; set; } = "25";
    public string Storage { get; set; } = "69";
    public string Target { get; set; } = "69";
    public string? SourceSlotId { get; set; }
    public string? TargetSlotId { get; set; }

    // 统计指标
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double TotalWalkTimeCostSec { get; set; } = 0.0;
    public double TotalCargoActionTimeCostSec { get; set; } = 0.0;
    public double TotalWaitingTimeCostSec { get; set; } = 0.0;
    public double TotalOdomMeter { get; set; } = 0.0;
    public double MileageKm => TotalOdomMeter / 1000.0;

    /// <summary>等待 / 失败 / 取消的原因说明</summary>
    public string? FailReason { get; set; }

    /// <summary>当前工步已自动重新规划的次数（走出拥堵后清零，不入库）</summary>
    public int AutoRetryCount { get; set; } = 0;

    public OrderStep? CurrentStep => CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count ? Steps[CurrentStepIndex] : null;

    public bool IsFinished => OrderStateMachine.IsTerminal(Status);
    public bool IsActive => !IsFinished;

    /// <summary>当前状态下允许的人工操作（前端按钮与接口校验共用）</summary>
    public IReadOnlyList<string> AllowedActions => OrderStateMachine.AllowedActions(Status);

    public TransportOrderAggregate()
    {
    }

    public TransportOrderAggregate(string orderSerial, int priority = 50, string? bindVehicleId = null)
    {
        OrderSerial = orderSerial;
        SubNo = orderSerial;
        Priority = priority;
        BindVehicleId = bindVehicleId;
    }

    // ───────────────────────── 状态转移 ─────────────────────────

    /// <summary>
    /// 状态转移的唯一入口。目标状态不在转移表内（含终态再变更）返回 false，状态保持不变。
    /// 进入已完成 / 已失败 / 已取消时写入完成时间；进入已完成 / 排队 / 已分配 / 执行中时清除原因说明。
    /// </summary>
    public bool TryTransition(OrderStatus to, DateTime? currentTimeUtc = null, string? reason = null)
    {
        lock (_gate)
        {
            if (Status == to)
            {
                // 同状态不算转移，但等待原因可能随情况更新
                if (to == OrderStatus.WaitingAction && !string.IsNullOrEmpty(reason)) FailReason = reason;
                return false;
            }

            if (!OrderStateMachine.CanTransition(Status, to)) return false;

            var time = currentTimeUtc ?? DateTime.UtcNow;
            Status = to;
            switch (to)
            {
                case OrderStatus.WaitingAction:
                    if (!string.IsNullOrEmpty(reason)) FailReason = reason;
                    break;
                case OrderStatus.Failed:
                    FailReason = !string.IsNullOrEmpty(reason) ? reason : (FailReason ?? "任务失败");
                    CompletedAt = time;
                    break;
                case OrderStatus.Cancelled:
                    FailReason = !string.IsNullOrEmpty(reason) ? reason : "任务已取消";
                    CompletedAt = time;
                    break;
                case OrderStatus.Done:
                    FailReason = null;
                    CompletedAt = time;
                    break;
                default:
                    FailReason = null;
                    break;
            }

            return true;
        }
    }

    public bool TryCancel(DateTime? currentTimeUtc = null, string? reason = null) =>
        TryTransition(OrderStatus.Cancelled, currentTimeUtc, reason);

    /// <summary>仅用于重启后从数据库还原订单，不做转移校验。</summary>
    public void RestoreStatus(OrderStatus status)
    {
        lock (_gate)
        {
            Status = status;
        }
    }

    // ───────────────────────── 工步推进 ─────────────────────────

    public void AdvanceStep(DateTime? currentTimeUtc = null)
    {
        lock (_gate)
        {
            if (IsFinished) return;

            DateTime time = currentTimeUtc ?? DateTime.UtcNow;
            var current = CurrentStep;
            if (current != null)
            {
                current.FinishedAt = time;

                if (current.IsTerminal || current.NextStepId == "END" || current.NextStepId == "FINISH")
                {
                    Complete(time);
                    return;
                }

                if (!string.IsNullOrEmpty(current.NextStepId))
                {
                    JumpToStepById(current.NextStepId, currentTimeUtc);
                    return;
                }
            }

            CurrentStepIndex++;
            if (CurrentStepIndex >= Steps.Count)
            {
                Complete(time);
            }
            else
            {
                Steps[CurrentStepIndex].StartedAt = time;
            }
        }
    }

    public void JumpToStep(int targetIndex, DateTime? currentTimeUtc = null)
    {
        lock (_gate)
        {
            if (IsFinished) return;

            DateTime time = currentTimeUtc ?? DateTime.UtcNow;
            if (CurrentStep != null)
            {
                CurrentStep.FinishedAt = time;
            }

            CurrentStepIndex = targetIndex;
            if (CurrentStepIndex >= Steps.Count)
            {
                Complete(time);
            }
            else
            {
                Steps[CurrentStepIndex].StartedAt = time;
            }
        }
    }

    public void JumpToStepById(string targetStepId, DateTime? currentTimeUtc = null)
    {
        lock (_gate)
        {
            if (IsFinished) return;

            if (targetStepId == "END" || targetStepId == "FINISH")
            {
                DateTime time = currentTimeUtc ?? DateTime.UtcNow;
                if (CurrentStep != null) CurrentStep.FinishedAt = time;
                Complete(time);
                return;
            }

            int idx = Steps.FindIndex(s => s.StepId == targetStepId);
            if (idx >= 0)
            {
                JumpToStep(idx, currentTimeUtc);
            }
            else
            {
                AdvanceStep(currentTimeUtc);
            }
        }
    }

    /// <summary>把当前工步标记为「已跳过」。调用方随后调用 <see cref="AdvanceStep"/> 进入下一步。</summary>
    public bool MarkCurrentStepSkipped(DateTime? currentTimeUtc = null)
    {
        lock (_gate)
        {
            if (IsFinished || CurrentStep == null) return false;
            CurrentStep.IsSkipped = true;
            CurrentStep.FinishedAt = currentTimeUtc ?? DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>清除当前工步的执行痕迹，让它从头再执行一遍。</summary>
    public bool RestartCurrentStep()
    {
        lock (_gate)
        {
            if (IsFinished || CurrentStep == null) return false;
            var step = CurrentStep;
            step.StartedAt = null;
            step.FinishedAt = null;
            step.IsSkipped = false;
            step.IsActionPermitted = false;
            step.IsRfidVerified = false;
            return true;
        }
    }

    private void Complete(DateTime time)
    {
        // 直接调用 AdvanceStep 的场景下订单可能还在排队，先补上「已分配」这一步
        if (Status == OrderStatus.Queued) TryTransition(OrderStatus.Assigned, time);
        TryTransition(OrderStatus.Done, time);
    }

    // ───────────────────────── 工步状态（推导） ─────────────────────────

    /// <summary>
    /// 第 index 个工步当前的状态。完全由订单状态、当前下标与工步自身的完成 / 跳过标记决定，
    /// 因此订单被取消后，停在的那一步与后面的工步都显示为已取消，不会出现订单已终止而工步仍在等待的情况。
    /// </summary>
    public OrderStepStatus GetStepStatus(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= Steps.Count) return OrderStepStatus.Pending;

            var step = Steps[index];
            if (step.IsSkipped) return OrderStepStatus.Skipped;

            // 已经走过的工步：做完的是已完成，被流程分支绕过、从未执行的是已跳过
            if (Status == OrderStatus.Done || index < CurrentStepIndex)
            {
                return step.FinishedAt.HasValue ? OrderStepStatus.Completed : OrderStepStatus.Skipped;
            }

            if (index == CurrentStepIndex)
            {
                return Status switch
                {
                    OrderStatus.Running => OrderStepStatus.Running,
                    OrderStatus.WaitingAction => OrderStepStatus.Waiting,
                    OrderStatus.Cancelled => OrderStepStatus.Cancelled,
                    OrderStatus.Failed => OrderStepStatus.Failed,
                    _ => OrderStepStatus.Pending
                };
            }

            // 还没轮到的工步：订单仍在进行时待执行，订单终止后不会再执行
            return IsFinished ? OrderStepStatus.Cancelled : OrderStepStatus.Pending;
        }
    }

    /// <summary>已完成 + 已跳过的工步数占总工步数的比例；订单完成为 1。</summary>
    public double Progress
    {
        get
        {
            lock (_gate)
            {
                if (Status == OrderStatus.Done) return 1.0;
                if (Steps.Count == 0) return 0.0;

                int passed = 0;
                for (int i = 0; i < Steps.Count; i++)
                {
                    var s = GetStepStatus(i);
                    if (s == OrderStepStatus.Completed || s == OrderStepStatus.Skipped) passed++;
                }

                return Math.Round((double)passed / Steps.Count, 3);
            }
        }
    }

    /// <summary>接口层工步快照；状态仍由 <see cref="GetStepStatus"/> 推导，不另存一份。</summary>
    public IReadOnlyList<DetailedStepSnapshot> DetailedSteps
    {
        get
        {
            lock (_gate)
            {
                return Steps.Select((step, index) => new DetailedStepSnapshot
                {
                    No = step.StepIndex,
                    Name = step.Name,
                    Type = step.Action.ToString(),
                    Status = GetStepStatus(index).ToString(),
                    Parameter = string.IsNullOrWhiteSpace(step.TargetSlotId) ? step.TargetNodeId : step.TargetSlotId!,
                    FinishedAt = step.FinishedAt
                }).ToArray();
            }
        }
    }
}

public sealed class DetailedStepSnapshot
{
    public int No { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string Parameter { get; init; } = "";
    public DateTime? FinishedAt { get; init; }
}
