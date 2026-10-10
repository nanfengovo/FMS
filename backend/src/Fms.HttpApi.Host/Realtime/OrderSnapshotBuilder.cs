using Fms.Domain.Orders;
using Fms.Domain.Shared.Enums;
using Fms.Engine;

namespace Fms.HttpApi.Host.Realtime;

/// <summary>
/// 把引擎内的订单整理成前端可直接渲染的快照：来源、起止站点、当前工步、进度，
/// 以及执行车辆剩余的行驶路线（抽稀后），用于大屏/地图实时展示任务闭环。
/// </summary>
public static class OrderSnapshotBuilder
{
    private const int MaxFinishedKept = 30;
    private const int MaxRoutePoints = 48;

    public static object[] Build(DispatchEngine engine)
    {
        var all = engine.Orders.Values.OrderByDescending(o => o.CreatedAt).ToList();
        var active = all.Where(o => !o.IsFinished).ToList();
        var finished = all.Where(o => o.IsFinished).Take(MaxFinishedKept).ToList();

        return active.Concat(finished).Select(o => Describe(engine, o)).ToArray();
    }

    public static object Describe(DispatchEngine engine, TransportOrderAggregate o)
    {
        var steps = o.Steps;
        var sourceStep = steps.FirstOrDefault(s => s.Action == ActionType.LiftUp && !string.IsNullOrEmpty(s.TargetNodeId));
        var targetStep = steps.LastOrDefault(s => !string.IsNullOrEmpty(s.TargetNodeId));
        var current = o.CurrentStep;

        double progress = o.Status == OrderStatus.Done
            ? 1.0
            : steps.Count == 0 ? 0.0 : Math.Min(1.0, (double)o.CurrentStepIndex / steps.Count);

        return new
        {
            orderId = o.Id,
            serial = o.OrderSerial,
            origin = Origin(o),
            taskType = o.TaskType,
            status = o.Status.ToString(),
            priority = o.Priority,
            assignedVehicleId = o.AssignedVehicleId,
            bindVehicleId = o.BindVehicleId,
            sourceNodeId = sourceStep?.TargetNodeId ?? string.Empty,
            targetNodeId = targetStep?.TargetNodeId ?? string.Empty,
            currentStep = o.CurrentStepIndex,
            totalSteps = steps.Count,
            stepName = current?.Name ?? string.Empty,
            stepAction = current?.Action.ToString() ?? string.Empty,
            stepTargetNodeId = current?.TargetNodeId ?? string.Empty,
            progress = Math.Round(progress, 3),
            failReason = o.FailReason,
            createdAt = o.CreatedAt.ToString("O"),
            startedAt = o.StartedAt?.ToString("O"),
            completedAt = o.CompletedAt?.ToString("O"),
            route = o.IsFinished ? null : Route(engine, o)
        };
    }

    private static string Origin(TransportOrderAggregate o)
    {
        var t = (o.BulkTaskType ?? string.Empty).ToLowerInvariant();
        if (t == "internal") return "internal";
        if (t.Contains("auto") || t.Contains("workflow")) return "system";
        return "external";
    }

    private static double[][]? Route(DispatchEngine engine, TransportOrderAggregate o)
    {
        if (string.IsNullOrEmpty(o.AssignedVehicleId) || !engine.Vehicles.TryGetValue(o.AssignedVehicleId, out var v)) return null;

        try
        {
            var pts = v.TrajectoryPoints;
            int from = Math.Clamp(v.CurrentTrajectoryIndex, 0, pts.Count);
            int count = pts.Count - from;
            if (count <= 0) return null;

            int stride = Math.Max(1, (int)Math.Ceiling(count / (double)MaxRoutePoints));
            var list = new List<double[]>(Math.Min(count, MaxRoutePoints + 1))
            {
                new[] { Math.Round(v.Pose.X, 2), Math.Round(v.Pose.Y, 2) }
            };
            for (int i = from; i < pts.Count; i += stride)
            {
                list.Add(new[] { Math.Round(pts[i].X, 2), Math.Round(pts[i].Y, 2) });
            }
            var last = pts[^1];
            list.Add(new[] { Math.Round(last.X, 2), Math.Round(last.Y, 2) });
            return list.ToArray();
        }
        catch (Exception)
        {
            // 路线在引擎线程内被改写时本帧放弃，下一帧自然恢复
            return null;
        }
    }
}
