using System.Collections.Concurrent;
using Fms.Domain.Logging;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Vehicles;

namespace Fms.Engine;

/// <summary>
/// 订单人工操作与终态善后。
/// 所有状态变更都经由 <see cref="TransportOrderAggregate.TryTransition"/> 校验：终态（已完成/已失败/已取消）不可再变，
/// 对终态订单的取消 / 跳步 / 重做 / 中止 / 暂停 / 重试一律被拒绝（接口返回 409）。
/// 每个操作在受理时和在引擎线程真正执行时各校验一次，排队期间订单若已结束，操作自动作废。
/// </summary>
public partial class DispatchEngine
{
    /// <summary>被操作员「暂停任务」暂停的车辆 → 订单；订单结束时据此解除暂停，避免车辆永久卡住。</summary>
    private readonly ConcurrentDictionary<string, string> _operatorPausedVehicles = new();

    /// <summary>订单状态转移的引擎侧入口，终态转移会写入决策日志。</summary>
    private bool Transition(TransportOrderAggregate order, OrderStatus to, string? reason = null)
    {
        var from = order.Status;
        if (!order.TryTransition(to, Clock.CurrentTimeUtc, reason)) return false;

        if (OrderStateMachine.IsTerminal(to))
        {
            DecisionLogger?.Record(
                DecisionCategory.OrderDispatch,
                order.Id,
                "OrderTransition",
                OrderStateMachine.Label(to),
                $"订单 {order.OrderSerial}：{OrderStateMachine.Label(from)} → {OrderStateMachine.Label(to)}"
                + (string.IsNullOrEmpty(reason) ? string.Empty : $"（{reason}）"));
        }

        return true;
    }

    /// <summary>
    /// 让订单进入失败 / 取消终态：状态立即生效，车辆释放、货位预约撤销、事件通知在引擎线程内随后完成。
    /// </summary>
    private bool EndOrder(TransportOrderAggregate order, OrderStatus terminal, string reason)
    {
        if (!Transition(order, terminal, reason)) return false;

        _commandQueue.Enqueue(() =>
        {
            FinalizeTerminalOrder(order);
            if (terminal == OrderStatus.Cancelled)
            {
                OrderCancelled?.Invoke(this, order);
            }
            else
            {
                OrderFailed?.Invoke(this, order);
            }

            // 空出来的车可以马上接新单
            TryDispatchOrders();
        });

        return true;
    }

    /// <summary>失败 / 取消后的善后：作废排期事件、撤销货位预约、把车辆还给调度。</summary>
    private void FinalizeTerminalOrder(TransportOrderAggregate order)
    {
        CancelScheduledEventsForOrder(order.Id);

        var released = StorageManager.ReleaseReservationsForOrder(order.Id);
        if (released.Count > 0)
        {
            DecisionLogger?.Record(
                DecisionCategory.StorageAllocation,
                order.Id,
                "ReleaseReservation",
                "Success",
                $"订单 {order.OrderSerial} {OrderStateMachine.Label(order.Status)}，释放货位预约 {string.Join("、", released)}");
        }

        var vehicle = ResolveBoundVehicle(order);
        if (vehicle != null)
        {
            ReleaseVehicleFromOrder(vehicle, order);
        }
    }

    /// <summary>订单当前真正绑定的车辆。车辆已经去服务别的订单时返回 null，绝不碰别人的车。</summary>
    private VehicleAggregate? ResolveBoundVehicle(TransportOrderAggregate order)
    {
        if (string.IsNullOrEmpty(order.AssignedVehicleId)) return null;
        if (!Vehicles.TryGetValue(order.AssignedVehicleId, out var vehicle)) return null;
        return vehicle.AssignedOrderId == order.Id ? vehicle : null;
    }

    private void ReleaseVehicleFromOrder(VehicleAggregate vehicle, TransportOrderAggregate order)
    {
        if (vehicle.AssignedOrderId != order.Id) return;

        ReleaseAllReservations(vehicle);
        vehicle.ClearRoute();
        vehicle.WaitUntilTime = null;
        vehicle.AssignedOrderId = null;

        if (_operatorPausedVehicles.TryGetValue(vehicle.Id, out var pausedBy) && pausedBy == order.Id)
        {
            _operatorPausedVehicles.TryRemove(vehicle.Id, out _);
            vehicle.IsPaused = false;
        }

        if (vehicle.IsEstop)
        {
            vehicle.Status = VehicleStatus.Fault;
        }
    }

    /// <summary>工步推进后的统一收尾：完成则通知上游，被终止则释放车辆，否则驱动车辆执行下一工步。</summary>
    private void ContinueAfterAdvance(VehicleAggregate vehicle, TransportOrderAggregate order)
    {
        if (order.Status == OrderStatus.Done)
        {
            vehicle.AssignedOrderId = null;
            OrderCompleted?.Invoke(this, order);
        }
        else if (order.IsFinished)
        {
            ReleaseVehicleFromOrder(vehicle, order);
        }
        else
        {
            ExecuteNextStep(vehicle, order);
        }
    }

    private void AdvanceAndContinue(VehicleAggregate vehicle, TransportOrderAggregate order)
    {
        // 终态订单的 AdvanceStep 是空操作，不会把已取消的订单改成已完成
        order.AdvanceStep(Clock.CurrentTimeUtc);
        ContinueAfterAdvance(vehicle, order);
    }

    /// <summary>累计订单的行驶里程与等待时间（路权/互斥区/让行造成的原地停留），供任务详情与上游回报使用。</summary>
    private void AccumulateOrderMetrics(VehicleAggregate vehicle, double dtSec)
    {
        if (string.IsNullOrEmpty(vehicle.AssignedOrderId) ||
            !Orders.TryGetValue(vehicle.AssignedOrderId, out var order) ||
            order.IsFinished)
        {
            return;
        }

        if (vehicle.CurrentSpeed > 0.05)
        {
            order.TotalOdomMeter += vehicle.CurrentSpeed * dtSec;
        }
        else if (order.Status == OrderStatus.WaitingAction ||
                 (vehicle.Status == VehicleStatus.Running && vehicle.TrajectoryPoints.Count > 0))
        {
            order.TotalWaitingTimeCostSec += dtSec;
        }
    }

    // ───────────────────────── 人工操作 ─────────────────────────

    /// <summary>取消任务。已取消再次取消按幂等处理；已完成 / 已失败的任务不能取消。</summary>
    public OrderCommandResult CancelOrder(string orderId, string reason = "Cancelled by user")
    {
        Orders.TryGetValue(orderId, out var order);
        if (order != null && order.Status == OrderStatus.Cancelled)
        {
            return OrderCommandResult.AlreadyInState($"任务 {order.OrderSerial} 已经是已取消状态");
        }

        var rejected = OrderCommandResult.Check(order, OrderStateMachine.ActionCancel);
        if (rejected != null) return rejected;

        if (!EndOrder(order!, OrderStatus.Cancelled, reason))
        {
            // 与引擎线程抢状态时输了：以最新状态为准再判断一次
            return order!.Status == OrderStatus.Cancelled
                ? OrderCommandResult.AlreadyInState($"任务 {order.OrderSerial} 已经是已取消状态")
                : OrderCommandResult.Rejected($"任务 {order.OrderSerial} {OrderStateMachine.Label(order.Status)}，不能再取消");
        }

        return OrderCommandResult.Accepted($"任务 {order!.OrderSerial} 已取消");
    }

    /// <summary>跳过当前工步，直接进入下一工步。</summary>
    public OrderCommandResult SkipStep(string orderId)
    {
        Orders.TryGetValue(orderId, out var order);
        var rejected = OrderCommandResult.Check(order, OrderStateMachine.ActionSkip);
        if (rejected != null) return rejected;

        var target = order!;
        _commandQueue.Enqueue(() =>
        {
            if (target.IsFinished) return;

            CancelScheduledEventsForOrder(target.Id);
            target.MarkCurrentStepSkipped(Clock.CurrentTimeUtc);
            target.AdvanceStep(Clock.CurrentTimeUtc);

            var vehicle = ResolveBoundVehicle(target);
            if (vehicle != null)
            {
                ReleaseAllReservations(vehicle);
                vehicle.ClearRoute();
                vehicle.WaitUntilTime = null;
            }

            if (!target.IsFinished) OrderStepUpdated?.Invoke(this, target);
            if (vehicle != null) ContinueAfterAdvance(vehicle, target);
            else if (target.Status == OrderStatus.Done) OrderCompleted?.Invoke(this, target);
        });

        return OrderCommandResult.Accepted($"任务 {target.OrderSerial} 已跳过当前工步");
    }

    /// <summary>重做当前工步：清掉当前工步的执行痕迹，车辆从头再执行一遍。</summary>
    public OrderCommandResult ResetStep(string orderId)
    {
        Orders.TryGetValue(orderId, out var order);
        var rejected = OrderCommandResult.Check(order, OrderStateMachine.ActionResetStep);
        if (rejected != null) return rejected;

        var target = order!;
        if (ResolveBoundVehicle(target) == null)
        {
            return OrderCommandResult.Rejected($"任务 {target.OrderSerial} 还没有分配车辆，无法重做当前工步");
        }

        _commandQueue.Enqueue(() =>
        {
            if (target.IsFinished) return;
            var vehicle = ResolveBoundVehicle(target);
            if (vehicle == null) return;

            CancelScheduledEventsForOrder(target.Id);
            target.RestartCurrentStep();
            ReleaseAllReservations(vehicle);
            vehicle.ClearRoute();
            vehicle.WaitUntilTime = null;
            OrderStepUpdated?.Invoke(this, target);
            ExecuteNextStep(vehicle, target);
        });

        return OrderCommandResult.Accepted($"任务 {target.OrderSerial} 正在重做当前工步");
    }

    /// <summary>中止当前工步：当前工步无法继续，任务以「已失败」结束，车辆释放。</summary>
    public OrderCommandResult AbortStep(string orderId, string reason = "操作员中止当前工步")
    {
        Orders.TryGetValue(orderId, out var order);
        var rejected = OrderCommandResult.Check(order, OrderStateMachine.ActionAbortStep);
        if (rejected != null) return rejected;

        var target = order!;
        var step = target.CurrentStep;
        var where = step == null ? "当前工步" : $"第 {target.CurrentStepIndex + 1} 步「{step.Name}」";
        var failReason = $"{reason}：{where}";

        if (!EndOrder(target, OrderStatus.Failed, failReason))
        {
            return OrderCommandResult.Rejected($"任务 {target.OrderSerial} {OrderStateMachine.Label(target.Status)}，不能再中止工步");
        }

        return OrderCommandResult.Accepted($"任务 {target.OrderSerial} 已中止{where}，任务失败");
    }

    /// <summary>暂停 / 恢复任务（暂停执行该任务的车辆）。返回操作后的暂停状态。</summary>
    public (OrderCommandResult Result, bool IsPaused) TogglePauseOrder(string orderId)
    {
        Orders.TryGetValue(orderId, out var order);
        var rejected = OrderCommandResult.Check(order, OrderStateMachine.ActionPause);
        if (rejected != null) return (rejected, false);

        var target = order!;
        var vehicle = ResolveBoundVehicle(target);
        if (vehicle == null)
        {
            return (OrderCommandResult.Rejected($"任务 {target.OrderSerial} 还没有分配车辆，无法暂停"), false);
        }

        bool pause = !vehicle.IsPaused;
        _commandQueue.Enqueue(() =>
        {
            if (target.IsFinished) return;
            var bound = ResolveBoundVehicle(target);
            if (bound == null) return;

            bound.IsPaused = pause;
            if (pause)
            {
                _operatorPausedVehicles[bound.Id] = target.Id;
            }
            else
            {
                _operatorPausedVehicles.TryRemove(bound.Id, out _);
            }
        });

        return (OrderCommandResult.Accepted(pause ? $"任务 {target.OrderSerial} 已暂停" : $"任务 {target.OrderSerial} 已恢复"), pause);
    }

    /// <summary>重试当前工步（故障恢复矩阵）。仅对进行中的任务有效。</summary>
    public bool RetryStep(string orderId)
    {
        Orders.TryGetValue(orderId, out var order);
        if (OrderCommandResult.Check(order, OrderStateMachine.ActionRetry) != null) return false;

        var target = order!;
        if (target.CurrentStep == null || ResolveBoundVehicle(target) == null) return false;

        _commandQueue.Enqueue(() =>
        {
            if (target.IsFinished) return;
            var vehicle = ResolveBoundVehicle(target);
            if (vehicle == null) return;

            CancelScheduledEventsForOrder(target.Id);
            Transition(target, OrderStatus.Assigned);
            vehicle.Status = VehicleStatus.Idle;
            ExecuteNextStep(vehicle, target);
        });

        return true;
    }

    /// <summary>安全回退：车辆撤退到安全站点，任务进入等待状态。仅对进行中的任务有效。</summary>
    public bool SafeRollbackOrder(string orderId, string? fallbackStationId = null)
    {
        Orders.TryGetValue(orderId, out var order);
        if (OrderCommandResult.Check(order, OrderStateMachine.ActionRetry) != null) return false;

        var target = order!;
        if (ResolveBoundVehicle(target) == null) return false;

        _commandQueue.Enqueue(() =>
        {
            if (target.IsFinished) return;
            var vehicle = ResolveBoundVehicle(target);
            if (vehicle == null) return;

            CancelScheduledEventsForOrder(target.Id);
            ReleaseAllReservations(vehicle);
            vehicle.ClearRoute();

            string retreatStation = fallbackStationId ?? vehicle.CurrentNodeId ?? "PARK-01";
            var retreatRoute = PlanRoute(
                vehicle.CurrentNodeId ?? Graph.FindNearestNode(vehicle.Pose.Position, 10.0)?.Id ?? retreatStation,
                retreatStation,
                vehicle.Kinematics);
            if (retreatRoute.IsSuccess)
            {
                vehicle.AssignRoute(target.Id, retreatRoute.PathIds, retreatRoute.SampledPoints, retreatStation, retreatRoute.SampledIsBackward);
            }

            Transition(target, OrderStatus.WaitingAction, $"执行安全回退撤退至安全站点: {retreatStation}");
        });

        return true;
    }
}
