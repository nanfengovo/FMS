using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Logging;

public interface IDecisionLogService
{
    void Record(DecisionCategory category, string subjectId, string action, string outcome, string reason, object? context = null);
    (IReadOnlyList<DecisionRecord> Items, int TotalCount) Query(DecisionCategory? category = null, string? subjectId = null, int limit = 100);
}
