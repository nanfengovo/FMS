using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Traffic;

/// <summary>
/// 行程分段：包含几何边、运动学模式（前进/倒车/横移/自旋）、速度建议、时间窗与交管区需求
/// </summary>
public class ItinerarySegment
{
    public int SegmentIndex { get; set; }
    public string PathId { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public SegmentMotionMode MotionMode { get; set; } = SegmentMotionMode.Forward;

    public List<Point2D> TrajectoryPoints { get; set; } = new();
    public List<bool> TrajectoryBackwardFlags { get; set; } = new();

    public double RoadWidth { get; set; } = 1.2;
    public double MaxAllowedSpeed { get; set; } = 1.5;
    public double RecommendedSpeed { get; set; } = 1.2;

    public double StartHeadingRad { get; set; } = 0.0;
    public double EndHeadingRad { get; set; } = 0.0;
    public double LengthMeters { get; set; } = 0.0;
    public double EstimatedDurationSec { get; set; } = 0.0;

    public DateTime EstimatedEntryTimeUtc { get; set; }
    public DateTime EstimatedExitTimeUtc { get; set; }

    /// <summary>
    /// 若此段跨越或从属于某多边形交管区，则记录关联的 ZoneId
    /// </summary>
    public string? RequiredZoneId { get; set; }

    public bool IsBackward => MotionMode == SegmentMotionMode.Backward;
    public bool IsLateral => MotionMode == SegmentMotionMode.LateralShift;
}
