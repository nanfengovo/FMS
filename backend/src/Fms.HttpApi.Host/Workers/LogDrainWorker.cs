using Fms.Application.Logging;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;

namespace Fms.HttpApi.Host.Workers;

public class LogDrainWorker : BackgroundService
{
    private readonly LogSink _sink;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LogDrainWorker> _logger;

    public LogDrainWorker(LogSink sink, IServiceScopeFactory scopes, ILogger<LogDrainWorker> logger)
    {
        _sink = sink;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sink.Enqueue(new Fms.Domain.Logging.LogWrite
        {
            Category = Fms.Domain.Logging.LogCategory.Runtime,
            Level = "Information",
            Source = "LogDrainWorker",
            Actor = "SYSTEM",
            Message = "日志中心已启动"
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = new List<Fms.Domain.Logging.LogWrite>();
                var first = await _sink.Reader.ReadAsync(stoppingToken);
                batch.Add(first);
                while (batch.Count < 100 && _sink.Reader.TryRead(out var next))
                    batch.Add(next);

                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                foreach (var item in batch)
                {
                    db.LogEvents.Add(new LogEventRecord
                    {
                        Timestamp = DateTime.UtcNow,
                        Category = item.Category.ToString(),
                        Level = item.Level,
                        Source = item.Source,
                        Actor = item.Actor,
                        Message = item.Message,
                        PayloadJson = item.PayloadJson,
                        TraceId = item.TraceId,
                        Method = item.Method,
                        Path = item.Path,
                        StatusCode = item.StatusCode,
                        DurationMs = item.DurationMs,
                        Success = item.Success
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "日志落库失败");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
