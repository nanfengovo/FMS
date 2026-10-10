using System.Collections.Concurrent;
using Fms.Domain.Logging;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Logging;

public interface IAuditLogService
{
    void Log(string @operator, string action, string resource, string details, string ipAddress = "127.0.0.1", bool success = true);
    (IReadOnlyList<AuditLogEntry> Items, int TotalCount) Query(string? action = null, string? @operator = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50);
}

public class AuditLogService : IAuditLogService
{
    private readonly ConcurrentBag<AuditLogEntry> _logs = new();
    private readonly ILogger<AuditLogService>? _logger;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogSink? _sink;
    private readonly System.Threading.Channels.Channel<AuditLogRecord> _channel = System.Threading.Channels.Channel.CreateBounded<AuditLogRecord>(new System.Threading.Channels.BoundedChannelOptions(1000)
    {
        FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
    });

    public AuditLogService(ILogger<AuditLogService>? logger = null, IServiceScopeFactory? scopeFactory = null, ILogSink? sink = null)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sink = sink;
        SeedInitialLogs();

        if (_scopeFactory != null)
        {
            Task.Run(ProcessQueueAsync);
        }
    }

    private async Task ProcessQueueAsync()
    {
        var batch = new List<AuditLogRecord>();
        while (true)
        {
            try
            {
                batch.Clear();
                var first = await _channel.Reader.ReadAsync();
                batch.Add(first);

                while (batch.Count < 20 && _channel.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (_scopeFactory != null && batch.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                    db.AuditLogs.AddRange(batch);
                    await db.SaveChangesAsync();
                }

                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[AuditLogService] Failed to batch write audit logs");
                await Task.Delay(1000);
            }
        }
    }

    private void SeedInitialLogs()
    {
        // 不预置演示审计，空库保持空态，由真实登录与订单事件写入。
    }

    public void Log(string @operator, string action, string resource, string details, string ipAddress = "127.0.0.1", bool success = true)
    {
        var entry = new AuditLogEntry
        {
            Operator = @operator,
            Action = action,
            Resource = resource,
            Details = details,
            IpAddress = ipAddress,
            Success = success,
            Timestamp = DateTime.UtcNow
        };
        _logs.Add(entry);
        _sink?.Enqueue(new LogWrite
        {
            Category = LogCategory.UserOperation,
            Level = success ? "Information" : "Warning",
            Source = "Audit",
            Actor = @operator,
            Message = $"{action} {resource} {details}",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { action, resource, ipAddress, success }),
            Success = success
        });
        _logger?.LogInformation("[AUDIT] User={User} Action={Action} Resource={Resource} Success={Success}",
            @operator, action, resource, success);

        if (_scopeFactory != null)
        {
            _channel.Writer.TryWrite(new AuditLogRecord
            {
                Operator = @operator,
                ActionType = action,
                EntityType = resource,
                EntityId = resource,
                Details = details,
                Success = success,
                Timestamp = entry.Timestamp
            });
        }
    }

    public (IReadOnlyList<AuditLogEntry> Items, int TotalCount) Query(
        string? action = null,
        string? @operator = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                var q = db.AuditLogs.AsQueryable();

                if (!string.IsNullOrEmpty(action))
                {
                    q = q.Where(l => l.ActionType.Contains(action));
                }

                if (!string.IsNullOrEmpty(@operator))
                {
                    q = q.Where(l => l.Operator.Contains(@operator));
                }

                if (from.HasValue)
                {
                    q = q.Where(l => l.Timestamp >= from.Value);
                }

                if (to.HasValue)
                {
                    q = q.Where(l => l.Timestamp <= to.Value);
                }

                int totalCount = q.Count();
                var dbRecords = q.OrderByDescending(l => l.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var dbEntries = dbRecords.Select(l => new AuditLogEntry
                {
                    Id = Guid.TryParse(l.Id, out var gid) ? gid : Guid.NewGuid(),
                    Operator = l.Operator,
                    Action = l.ActionType,
                    Resource = l.EntityId,
                    Details = l.Details,
                    Success = l.Success,
                    Timestamp = l.Timestamp
                }).ToList();

                return (dbEntries, totalCount);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to query audit logs from database, falling back to in-memory buffer");
            }
        }

        var query = _logs.AsQueryable();

        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(l => l.Action.Contains(action, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(@operator))
        {
            query = query.Where(l => l.Operator.Contains(@operator, StringComparison.OrdinalIgnoreCase));
        }

        if (from.HasValue)
        {
            query = query.Where(l => l.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(l => l.Timestamp <= to.Value);
        }

        int total = query.Count();
        var paged = query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (paged, total);
    }
}
