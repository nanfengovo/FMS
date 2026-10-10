using Fms.Domain.Shared.Enums;

namespace Fms.Domain.Traffic;

/// <summary>
/// 资源时空租约：调度核心对窄道、边、交管区、站点道口的原子独占与时间窗预约
/// </summary>
public class ResourceLease
{
    public string LeaseId { get; set; } = Guid.NewGuid().ToString("N");
    public string VehicleId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;

    public ResourceType ResourceType { get; set; } = ResourceType.PathEdge;
    public string ResourceId { get; set; } = string.Empty;

    public string? FromNodeId { get; set; }
    public string? ToNodeId { get; set; }
    public string Direction { get; set; } = "forward";

    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }

    /// <summary>
    /// 优先级得分：载货接近目标 > 低电回充 > 载货搬运 > 普通空车 > 距目标较近
    /// </summary>
    public double PriorityScore { get; set; } = 0.0;

    public bool IsActive { get; set; } = true;

    public bool IsOpposingDirection(string from, string to)
    {
        if (string.IsNullOrEmpty(FromNodeId) || string.IsNullOrEmpty(ToNodeId)) return false;
        return FromNodeId == to && ToNodeId == from;
    }
}
