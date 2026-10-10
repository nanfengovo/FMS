using Fms.Application;
using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Domain.Vehicles;
using Fms.Engine;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fms.HttpApi.Host.Workers;

public class EnginePersistenceWorker : BackgroundService
{
    private readonly DispatchEngine _engine;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnginePersistenceWorker> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public EnginePersistenceWorker(
        DispatchEngine engine,
        IServiceScopeFactory scopeFactory,
        ILogger<EnginePersistenceWorker> logger)
    {
        _engine = engine;
        _scopeFactory = scopeFactory;
        _logger = logger;

        // 订阅调度核心订单全生命周期事件
        _engine.OrderSubmitted += (_, order) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, order, "SUBMITTED"));
        _engine.OrderStarted += (_, args) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, args.Order, "STARTED"));
        _engine.OrderArrivedTarget += (_, args) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, args.Order, "ARRIVED"));
        _engine.OrderActionExecuting += (_, args) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, args.Order, "ACTION_EXECUTING"));
        _engine.OrderCompleted += (_, order) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, order, "COMPLETED"));
        _engine.OrderCancelled += (_, order) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, order, "CANCELLED"));
        _engine.OrderFailed += (_, order) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, order, "FAILED"));
        _engine.OrderStepUpdated += (_, order) => _ = SafeExecuteAsync(db => UpsertOrderAsync(db, order, "STEP_UPDATED"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fms Engine SQLite Persistence Worker started.");

        // 初始化启动时将当前引擎内的所有初始订单与车辆落库同步
        await SafeExecuteAsync(async db =>
        {
            foreach (var order in _engine.Orders.Values)
            {
                // 已结束的历史订单是从库里读出来的，状态没有变化，不需要再写一遍
                if (order.IsFinished) continue;
                await UpsertOrderAsync(db, order, "INITIAL_SYNC");
            }
            await SyncVehiclesAsync(db);
        });

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeExecuteAsync(SyncVehiclesAsync);
        }
    }

    private async Task SafeExecuteAsync(Func<FmsDbContext, Task> action)
    {
        await _dbLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            await action(db);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist FMS state to SQLite database.");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task UpsertOrderAsync(FmsDbContext db, TransportOrderAggregate order, string reason)
    {
        var record = await db.Orders
            .Include(o => o.Steps)
            .FirstOrDefaultAsync(o => o.Id == order.Id || o.OrderSerial == order.OrderSerial);

        if (record == null)
        {
            record = new TransportOrderRecord
            {
                Id = order.Id,
                OrderSerial = order.OrderSerial,
                TaskType = order.TaskType,
                BulkTaskType = order.BulkTaskType,
                Priority = order.Priority,
                BindVehicleId = order.BindVehicleId,
                AssignedVehicleId = order.AssignedVehicleId,
                Status = order.Status.ToString(),
                SourceNodeId = order.Steps.FirstOrDefault()?.TargetNodeId ?? order.Storage ?? string.Empty,
                TargetNodeId = order.Steps.LastOrDefault()?.TargetNodeId ?? order.Target ?? string.Empty,
                CreatedAt = order.CreatedAt,
                StartedAt = order.StartedAt,
                CompletedAt = order.CompletedAt,
                MovementTimeSec = order.TotalWalkTimeCostSec,
                WaitTimeSec = order.TotalWaitingTimeCostSec,
                MileageKm = order.MileageKm,
                FailReason = order.FailReason,
                SubNo = order.SubNo,
                TaskCodeA = order.TaskCodeA,
                TaskCodeB = order.TaskCodeB,
                FinishingAction = order.FinishingAction,
                CargoSerial = order.CargoSerial,
                ArmAction = order.ArmAction,
                Rfid = order.Rfid,
                Mark = order.Mark,
                OptionCode = order.OptionCode,
                PassPoint = order.PassPoint,
                PassPointToGoal = order.PassPointToGoal,
                RobotType = int.TryParse(order.RobotType, out var rt) ? rt : 99,
                Storage = order.Storage,
                Target = order.Target,
                OrderStepsJson = System.Text.Json.JsonSerializer.Serialize(order.Steps),
                CurrentStepIndex = order.CurrentStepIndex
            };

            // 同步工步
            SyncSteps(db, record, order);
            db.Orders.Add(record);
        }
        else
        {
            record.Status = order.Status.ToString();
            record.AssignedVehicleId = order.AssignedVehicleId;
            record.StartedAt = order.StartedAt ?? record.StartedAt;
            record.CompletedAt = order.CompletedAt ?? record.CompletedAt;
            record.MovementTimeSec = order.TotalWalkTimeCostSec;
            record.WaitTimeSec = order.TotalWaitingTimeCostSec;
            record.MileageKm = order.MileageKm;
            record.FailReason = order.FailReason;
            record.OrderStepsJson = System.Text.Json.JsonSerializer.Serialize(order.Steps);
            record.CurrentStepIndex = order.CurrentStepIndex;

            SyncSteps(db, record, order);
        }

        // 记录审计日志
        db.AuditLogs.Add(new AuditLogRecord
        {
            Timestamp = DateTime.UtcNow,
            Operator = "DISPATCH_ENGINE",
            ActionType = $"ORDER_{reason}",
            EntityType = "ORDER",
            EntityId = order.OrderSerial,
            Details = $"订单 {order.OrderSerial} 状态流转为 {order.Status} (车辆: {order.AssignedVehicleId ?? "未分配"})",
            Success = true
        });

        await db.SaveChangesAsync();
    }

    /// <summary>把订单的真实工步（序号、动作、目标站点、由订单状态推导出的工步状态）写入工步表。</summary>
    private static void SyncSteps(FmsDbContext db, TransportOrderRecord record, TransportOrderAggregate order)
    {
        for (int i = 0; i < order.Steps.Count; i++)
        {
            var step = order.Steps[i];
            int stepNo = i + 1;

            var stepRecord = record.Steps.FirstOrDefault(s => s.StepNo == stepNo);
            if (stepRecord == null)
            {
                stepRecord = new TaskStepRecord { OrderId = record.Id, StepNo = stepNo };
                record.Steps.Add(stepRecord);
            }

            stepRecord.Name = step.Name;
            stepRecord.Type = step.Action.ToString();
            stepRecord.Status = order.GetStepStatus(i).ToString();
            stepRecord.Parameter = DescribeStepParameter(step);
            stepRecord.FinishedAt = step.FinishedAt;
        }

        // 早期版本曾写入过 12 条与真实工步无关的模板行，这里一并清掉
        foreach (var extra in record.Steps.Where(s => s.StepNo > order.Steps.Count).ToList())
        {
            record.Steps.Remove(extra);
            if (db.Entry(extra).State != EntityState.Detached && db.Entry(extra).State != EntityState.Added)
            {
                db.TaskSteps.Remove(extra);
            }
        }
    }

    private static string DescribeStepParameter(OrderStep step)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(step.TargetNodeId)) parts.Add($"站点 {step.TargetNodeId}");
        if (!string.IsNullOrEmpty(step.TargetSlotId)) parts.Add($"货位 {step.TargetSlotId}");
        if (!string.IsNullOrEmpty(step.CargoId)) parts.Add($"货物 {step.CargoId}");
        if (step.DwellSec > 0 && step.Action is ActionType.Wait or ActionType.LiftUp or ActionType.PutDown or ActionType.CheckRfid)
        {
            parts.Add($"停留 {step.DwellSec:0.#} 秒");
        }
        return string.Join("，", parts);
    }

    private async Task SyncVehiclesAsync(FmsDbContext db)
    {
        await FmsAppService.UpsertVehiclesAsync(db, _engine.Vehicles.Values);
    }
}
