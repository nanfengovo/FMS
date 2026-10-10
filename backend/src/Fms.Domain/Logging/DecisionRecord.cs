using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Logging;

/// <summary>
/// 调度排障决策记录：专门记录调度内核“为什么让行、为什么没派单、卡在哪个租约、分支为何流转”
/// </summary>
public class DecisionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public DecisionCategory Category { get; set; } = DecisionCategory.Routing;
    public string SubjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
}
