using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;

namespace Fms.HttpApi.Host.Realtime;

/// <summary>
/// 订单对外的统一投影：任务列表、任务详情、大屏快照共用同一份状态、进度与工步数据，
/// 所有展示面看到的订单状态、工步状态和可用操作都来自聚合本身，不会各说各话。
/// </summary>
public static class OrderDtoFactory
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static string Origin(TransportOrderAggregate o)
    {
        var t = (o.BulkTaskType ?? string.Empty).ToLowerInvariant();
        if (t == "internal") return "internal";
        if (t.Contains("auto") || t.Contains("workflow")) return "system";
        return "external";
    }

    public static string SourceNodeId(TransportOrderAggregate o) =>
        o.Steps.FirstOrDefault(s => s.Action == ActionType.LiftUp && !string.IsNullOrEmpty(s.TargetNodeId))?.TargetNodeId ?? string.Empty;

    public static string TargetNodeId(TransportOrderAggregate o) =>
        o.Steps.LastOrDefault(s => !string.IsNullOrEmpty(s.TargetNodeId))?.TargetNodeId ?? string.Empty;

    /// <summary>任务中心列表 / 详情使用的完整投影（含真实工步及其状态）。</summary>
    public static object Describe(TransportOrderAggregate o)
    {
        var steps = o.Steps.ToList();

        return new
        {
            id = o.Id,
            serial = o.OrderSerial,
            bulkTaskType = o.BulkTaskType,
            origin = Origin(o),
            taskType = o.TaskType,
            priority = o.Priority,
            status = o.Status.ToString(),
            statusLabel = OrderStateMachine.Label(o.Status),
            isTerminal = o.IsFinished,
            allowedActions = o.AllowedActions,
            assignedVehicle = o.AssignedVehicleId,
            bindVehicleId = o.BindVehicleId,
            sourceNodeId = SourceNodeId(o),
            targetNodeId = TargetNodeId(o),
            currentStepIndex = o.CurrentStepIndex,
            totalSteps = steps.Count,
            currentStepName = o.IsFinished ? string.Empty : o.CurrentStep?.Name ?? string.Empty,
            progress = o.Progress,
            createdAt = o.CreatedAt.ToString(TimeFormat),
            startedAt = o.StartedAt?.ToString(TimeFormat),
            completedAt = o.CompletedAt?.ToString(TimeFormat),
            walkCostSec = Math.Round(o.TotalWalkTimeCostSec, 1),
            cargoCostSec = Math.Round(o.TotalCargoActionTimeCostSec, 1),
            waitCostSec = Math.Round(o.TotalWaitingTimeCostSec, 1),
            mileageKm = Math.Round(o.MileageKm, 3),
            failReason = o.FailReason,
            cargoSerial = o.CargoSerial,
            steps = steps.Select((s, index) =>
            {
                var status = o.GetStepStatus(index);
                return new
                {
                    stepIndex = index,
                    name = s.Name,
                    action = s.Action.ToString(),
                    targetNodeId = s.TargetNodeId,
                    targetSlotId = s.TargetSlotId,
                    optionCode = s.OptionCode,
                    cargoId = s.CargoId,
                    goalAction = s.GoalAction,
                    dwellSec = s.DwellSec,
                    status = status.ToString(),
                    statusLabel = OrderStateMachine.Label(status),
                    isCurrent = !o.IsFinished && index == o.CurrentStepIndex,
                    isCompleted = status == OrderStepStatus.Completed,
                    startedAt = s.StartedAt?.ToString(TimeFormat),
                    finishedAt = s.FinishedAt?.ToString(TimeFormat),
                    isActionPermitted = s.IsActionPermitted,
                    isRfidVerified = s.IsRfidVerified
                };
            }).ToList()
        };
    }
}
