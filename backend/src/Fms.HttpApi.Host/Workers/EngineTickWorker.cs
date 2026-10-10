using Fms.Application;
using Fms.Domain.Logging;
using Fms.HttpApi.Host.Hubs;
using Fms.HttpApi.Host.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Fms.HttpApi.Host.Workers;

public class EngineTickWorker : BackgroundService
{
    private readonly FmsAppService _fmsAppService;
    private readonly IHubContext<FmsRealtimeHub, IFmsClient> _hubContext;
    private readonly Fms.Application.Logging.IFlightRecorderService _flightRecorderService;
    private readonly ILogSink _logSink;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EngineTickWorker> _logger;

    public EngineTickWorker(
        FmsAppService fmsAppService,
        IHubContext<FmsRealtimeHub, IFmsClient> hubContext,
        Fms.Application.Logging.IFlightRecorderService flightRecorderService,
        ILogSink logSink,
        IServiceScopeFactory scopeFactory,
        ILogger<EngineTickWorker> logger)
    {
        _fmsAppService = fmsAppService;
        _hubContext = hubContext;
        _flightRecorderService = flightRecorderService;
        _logSink = logSink;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fms Dispatch Engine 100ms Tick Worker started.");
        const double dtSec = 0.1; // 100ms
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        // 监听冲突警报推送到前端
        _fmsAppService.Engine.ConflictResolved += async (_, msg) =>
        {
            await _hubContext.Clients.All.ReceiveConflictAlert(msg);
        };

        int rcsReportTickCounter = 0;
        int orderSnapshotTickCounter = 0;

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // 1. 步进调度与仿真引擎
                _fmsAppService.Engine.AdvanceTick(dtSec);
                PublishWorkflowLogs();
                await ArchiveFinishedScenesAsync();

                // 2. 遥测黑匣子高频帧记录 (Flight Recorder)
                long nowMs = _fmsAppService.Engine.Clock.CurrentTimestampMs;
                foreach (var v in _fmsAppService.Engine.Vehicles.Values)
                {
                    _flightRecorderService.RecordFrame(v, nowMs);
                }

                // 3. 批量打包当前车辆位姿与台账，10Hz 推送给渲染端与车队页
                var batch = Realtime.VehicleSnapshotBuilder.Build(_fmsAppService.Engine);

                if (batch.Length > 0)
                {
                    await _hubContext.Clients.All.ReceiveTelemetryBatch(batch);
                }

                // 3.1 每 500ms 推送一次任务快照：来源、起止站点、进度与剩余路线，前端无需轮询即可看到任务闭环
                orderSnapshotTickCounter++;
                if (orderSnapshotTickCounter >= 5)
                {
                    orderSnapshotTickCounter = 0;
                    await _hubContext.Clients.All.ReceiveOrderSnapshot(OrderSnapshotBuilder.Build(_fmsAppService.Engine));
                }

                // 3. 周期 1s (10 ticks) 向上位系统上报车辆状态 (对接 PDF 10. 上报车辆状态)
                rcsReportTickCounter++;
                if (rcsReportTickCounter >= 10)
                {
                    rcsReportTickCounter = 0;
                    if (_fmsAppService.RcsConfig.PushEnabled)
                    {
                        foreach (var vehicle in _fmsAppService.Engine.Vehicles.Values)
                        {
                            _ = _fmsAppService.RcsCallback.ReportRobotStatusAsync(vehicle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Engine Tick loop.");
            }
        }
    }

    private async Task ArchiveFinishedScenesAsync()
    {
        var pending = _fmsAppService.Engine.WorkflowManager.GetAllInstances()
            .Any(instance => Replay.ReplaySceneService.IsTerminal(instance.Status) && !instance.ReplaySaved);
        if (!pending) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Fms.EntityFrameworkCore.FmsDbContext>();
        var scenes = scope.ServiceProvider.GetRequiredService<Replay.ReplaySceneService>();
        await scenes.ArchiveFinishedAsync(_fmsAppService, db);
    }

    private void PublishWorkflowLogs()
    {
        foreach (var instance in _fmsAppService.Engine.WorkflowManager.GetAllInstances())
        {
            foreach (var line in instance.TakeNewLogs())
            {
                var failed = line.Contains("失败", StringComparison.Ordinal) || line.Contains("异常", StringComparison.Ordinal);
                _logSink.Enqueue(new LogWrite
                {
                    Category = LogCategory.Runtime,
                    Level = failed ? "Warning" : "Information",
                    Source = "Workflow",
                    Actor = instance.BindVehicleId ?? "scheduler",
                    Message = line,
                    TraceId = instance.InstanceId,
                    Path = $"/fms/workflow/{instance.DefinitionId}",
                    Success = !failed
                });
            }
        }
    }
}
