using System.Collections.Concurrent;
using System.Text.Json;
using Fms.Domain.Logging;
using Fms.Domain.Shared.Enums;
using Fms.EntityFrameworkCore;
using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fms.Application.Logging;

/// <summary>
/// 调度决策审计服务：高吞吐低开销环形缓冲记录内核决策链条，并自动持久化至 SQLite 数据库
/// </summary>
public class DecisionLogService : IDecisionLogService
{
    private readonly ConcurrentQueue<DecisionRecord> _records = new();
    private readonly int _maxCapacity;
    private readonly ILogger<DecisionLogService>? _logger;
    private readonly IServiceScopeFactory? _scopeFactory;

    private readonly System.Threading.Channels.Channel<DecisionLogRecord> _channel = System.Threading.Channels.Channel.CreateBounded<DecisionLogRecord>(new System.Threading.Channels.BoundedChannelOptions(5000)
    {
        FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
    });

    public DecisionLogService(
        int maxCapacity = 10000,
        ILogger<DecisionLogService>? logger = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _maxCapacity = maxCapacity;
        _logger = logger;
        _scopeFactory = scopeFactory;

        if (_scopeFactory != null)
        {
            Task.Run(PreloadFromDatabaseAsync);
            Task.Run(ProcessQueueAsync);
        }
    }

    private async Task ProcessQueueAsync()
    {
        var batch = new List<DecisionLogRecord>();
        while (true)
        {
            try
            {
                batch.Clear();
                var first = await _channel.Reader.ReadAsync();
                batch.Add(first);

                while (batch.Count < 50 && _channel.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (_scopeFactory != null && batch.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
                    db.DecisionLogs.AddRange(batch);
                    await db.SaveChangesAsync();
                }

                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[DecisionLogService] Failed to batch write decision logs");
                await Task.Delay(1000);
            }
        }
    }

    private async Task PreloadFromDatabaseAsync()
    {
        try
        {
            if (_scopeFactory == null) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            var recent = await db.DecisionLogs.AsNoTracking()
                .OrderByDescending(d => d.TimestampUtc)
                .Take(500)
                .ToListAsync();

            recent.Reverse();
            foreach (var r in recent)
            {
                if (Enum.TryParse<DecisionCategory>(r.Category, out var cat))
                {
                    _records.Enqueue(new DecisionRecord
                    {
                        TimestampUtc = r.TimestampUtc,
                        Category = cat,
                        SubjectId = r.SubjectId,
                        Action = r.Action,
                        Outcome = r.Outcome,
                        Reason = r.Reason,
                        ContextJson = r.ContextJson
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[DecisionLogService] Failed to preload logs from database");
        }
    }

    public void Record(DecisionCategory category, string subjectId, string action, string outcome, string reason, object? context = null)
    {
        string? ctxJson = null;
        if (context != null)
        {
            try
            {
                ctxJson = JsonSerializer.Serialize(context);
            }
            catch { }
        }

        var entry = new DecisionRecord
        {
            TimestampUtc = DateTime.UtcNow,
            Category = category,
            SubjectId = subjectId,
            Action = action,
            Outcome = outcome,
            Reason = reason,
            ContextJson = ctxJson
        };

        _records.Enqueue(entry);
        while (_records.Count > _maxCapacity)
        {
            _records.TryDequeue(out _);
        }

        if (_scopeFactory != null)
        {
            _channel.Writer.TryWrite(new DecisionLogRecord
            {
                TimestampUtc = entry.TimestampUtc,
                Category = entry.Category.ToString(),
                SubjectId = entry.SubjectId,
                Action = entry.Action,
                Outcome = entry.Outcome,
                Reason = entry.Reason,
                ContextJson = entry.ContextJson
            });
        }

        _logger?.LogDebug("[DECISION] Cat={Category} Sub={Subject} Act={Action} Outcome={Outcome} Reason={Reason}",
            category, subjectId, action, outcome, reason);
    }

    public (IReadOnlyList<DecisionRecord> Items, int TotalCount) Query(DecisionCategory? category = null, string? subjectId = null, int limit = 100)
    {
        var all = _records.ToArray();
        var filtered = all.AsEnumerable();

        if (category.HasValue)
        {
            filtered = filtered.Where(r => r.Category == category.Value);
        }

        if (!string.IsNullOrEmpty(subjectId))
        {
            filtered = filtered.Where(r => r.SubjectId.Contains(subjectId, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.OrderByDescending(r => r.TimestampUtc).ToList();
        return (filteredList.Take(limit).ToList(), filteredList.Count);
    }
}
