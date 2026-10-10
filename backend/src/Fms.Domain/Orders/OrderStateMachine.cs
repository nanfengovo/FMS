using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Orders;

/// <summary>
/// 订单状态机：全系统唯一的合法状态转移表。
/// 已完成 / 已失败 / 已取消是终态，没有任何出边；终态订单不再接受任何修改。
/// </summary>
public static class OrderStateMachine
{
    public const string ActionCancel = "cancel";
    public const string ActionSkip = "skip";
    public const string ActionPause = "pause";
    public const string ActionResetStep = "resetStep";
    public const string ActionAbortStep = "abortStep";
    public const string ActionRetry = "retry";

    private static readonly OrderStatus[] None = Array.Empty<OrderStatus>();

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Queued] = new[] { OrderStatus.Assigned, OrderStatus.Failed, OrderStatus.Cancelled },
            [OrderStatus.Assigned] = new[]
            {
                OrderStatus.Queued, OrderStatus.Running, OrderStatus.WaitingAction,
                OrderStatus.Done, OrderStatus.Failed, OrderStatus.Cancelled
            },
            [OrderStatus.Running] = new[]
            {
                OrderStatus.Assigned, OrderStatus.WaitingAction,
                OrderStatus.Done, OrderStatus.Failed, OrderStatus.Cancelled
            },
            [OrderStatus.WaitingAction] = new[]
            {
                OrderStatus.Assigned, OrderStatus.Running,
                OrderStatus.Done, OrderStatus.Failed, OrderStatus.Cancelled
            },
            [OrderStatus.Done] = None,
            [OrderStatus.Failed] = None,
            [OrderStatus.Cancelled] = None
        };

    private static readonly string[] QueuedActions = { ActionCancel };

    private static readonly string[] ActiveActions =
    {
        ActionCancel, ActionSkip, ActionPause, ActionResetStep, ActionAbortStep, ActionRetry
    };

    private static readonly string[] NoActions = Array.Empty<string>();

    public static bool IsTerminal(OrderStatus status) =>
        status == OrderStatus.Done || status == OrderStatus.Failed || status == OrderStatus.Cancelled;

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : None;

    /// <summary>当前状态下允许的人工操作。前端按钮、接口校验共用这一个来源。</summary>
    public static IReadOnlyList<string> AllowedActions(OrderStatus status) => status switch
    {
        OrderStatus.Queued => QueuedActions,
        OrderStatus.Assigned or OrderStatus.Running or OrderStatus.WaitingAction => ActiveActions,
        _ => NoActions
    };

    public static bool IsActionAllowed(OrderStatus status, string action) =>
        Array.IndexOf(status switch
        {
            OrderStatus.Queued => QueuedActions,
            OrderStatus.Assigned or OrderStatus.Running or OrderStatus.WaitingAction => ActiveActions,
            _ => NoActions
        }, action) >= 0;

    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Queued => "排队中",
        OrderStatus.Assigned => "已分配",
        OrderStatus.Running => "执行中",
        OrderStatus.WaitingAction => "等待中",
        OrderStatus.Done => "已完成",
        OrderStatus.Failed => "已失败",
        OrderStatus.Cancelled => "已取消",
        _ => status.ToString()
    };

    public static string Label(OrderStepStatus status) => status switch
    {
        OrderStepStatus.Pending => "待执行",
        OrderStepStatus.Running => "执行中",
        OrderStepStatus.Waiting => "等待中",
        OrderStepStatus.Completed => "已完成",
        OrderStepStatus.Skipped => "已跳过",
        OrderStepStatus.Cancelled => "已取消",
        OrderStepStatus.Failed => "已失败",
        _ => status.ToString()
    };

    public static string ActionLabel(string action) => action switch
    {
        ActionCancel => "取消",
        ActionSkip => "跳步",
        ActionPause => "暂停/恢复",
        ActionResetStep => "重做当前工步",
        ActionAbortStep => "中止当前工步",
        ActionRetry => "重试",
        _ => action
    };
}

public enum OrderCommandOutcome
{
    /// <summary>命令已受理，将在引擎下一拍执行。</summary>
    Accepted = 0,

    /// <summary>订单已经处于目标状态，重复提交按幂等处理。</summary>
    AlreadyInState = 1,

    NotFound = 2,

    /// <summary>当前状态不允许该操作（例如对终态订单跳步）。</summary>
    Rejected = 3
}

public sealed record OrderCommandResult(OrderCommandOutcome Outcome, string Message)
{
    public bool Success => Outcome == OrderCommandOutcome.Accepted || Outcome == OrderCommandOutcome.AlreadyInState;

    public static OrderCommandResult Accepted(string message) => new(OrderCommandOutcome.Accepted, message);
    public static OrderCommandResult AlreadyInState(string message) => new(OrderCommandOutcome.AlreadyInState, message);
    public static OrderCommandResult NotFound(string message) => new(OrderCommandOutcome.NotFound, message);
    public static OrderCommandResult Rejected(string message) => new(OrderCommandOutcome.Rejected, message);

    /// <summary>对订单执行某项人工操作前的统一校验。</summary>
    public static OrderCommandResult? Check(TransportOrderAggregate? order, string action)
    {
        if (order == null) return NotFound("任务不存在");
        if (OrderStateMachine.IsActionAllowed(order.Status, action)) return null;

        return Rejected(OrderStateMachine.IsTerminal(order.Status)
            ? $"任务 {order.OrderSerial} {OrderStateMachine.Label(order.Status)}，不能再{OrderStateMachine.ActionLabel(action)}"
            : $"任务 {order.OrderSerial} 当前为「{OrderStateMachine.Label(order.Status)}」，不能{OrderStateMachine.ActionLabel(action)}");
    }
}
