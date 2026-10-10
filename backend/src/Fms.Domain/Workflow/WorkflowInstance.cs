using System.Collections.Concurrent;

namespace Fms.Domain.Workflow;

public sealed class ReplayLogMark
{
    public ReplayLogMark(long simMs, string line)
    {
        SimMs = simMs;
        Line = line;
    }

    public long SimMs { get; set; }
    public string Line { get; set; } = string.Empty;
}

public enum WorkflowInstanceStatus
{
    Initialized = 0,
    Running = 1,
    WaitingIoSignal = 2,
    WaitingTransportOrder = 3,
    Suspended = 4,
    Completed = 5,
    Fault = 6,
    Terminated = 7,
    Cancelled = 8
}

/// <summary>
/// 业务流程实例：代表某次产线工序或搬运任务的实际推进状态机
/// </summary>
public class WorkflowInstance
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DefinitionId { get; set; } = string.Empty;
    public string VersionTag { get; set; } = "v1.0.0";
    public string? BindVehicleId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Initialized;
    public string CurrentNodeId { get; set; } = string.Empty;
    public string? ActiveTransportOrderId { get; set; }

    public ConcurrentDictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExecutionLogs { get; set; } = new();
    private int _publishedLogCount;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public long StartSimMs { get; set; }
    public long EndSimMs { get; set; }
    public long ObservedSimMs { get; set; }
    public bool ReplaySaved { get; set; }
    public List<ReplayLogMark> LogMarks { get; } = new();

    public void AppendLog(string message)
    {
        string entry = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}";
        lock (ExecutionLogs)
        {
            ExecutionLogs.Add(entry);
            LogMarks.Add(new ReplayLogMark(ObservedSimMs, entry));
        }
    }

    public IReadOnlyList<string> TakeNewLogs()
    {
        lock (ExecutionLogs)
        {
            if (_publishedLogCount >= ExecutionLogs.Count) return Array.Empty<string>();
            var fresh = ExecutionLogs.Skip(_publishedLogCount).ToList();
            _publishedLogCount = ExecutionLogs.Count;
            return fresh;
        }
    }
}
