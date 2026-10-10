using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Traffic;

/// <summary>
/// 结构化行程：调度内核向导航层与动力学层下发的唯一不可变时空运动指南
/// </summary>
public class Itinerary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string StartNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public double? TargetHeadingRad { get; set; }

    public DateTime PlannedAtUtc { get; set; } = DateTime.UtcNow;
    public double TotalDistanceMeters { get; set; } = 0.0;
    public double TotalDurationSec { get; set; } = 0.0;

    public List<ItinerarySegment> Segments { get; set; } = new();

    public List<string> AllPathIds => Segments.Select(s => s.PathId).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();

    public List<Point2D> FlattenPoints()
    {
        var pts = new List<Point2D>();
        foreach (var s in Segments)
        {
            if (pts.Count > 0 && s.TrajectoryPoints.Count > 0)
            {
                // 去除衔接重复点
                pts.AddRange(s.TrajectoryPoints.Skip(1));
            }
            else
            {
                pts.AddRange(s.TrajectoryPoints);
            }
        }
        return pts;
    }

    public List<bool> FlattenBackwardFlags()
    {
        var flags = new List<bool>();
        foreach (var s in Segments)
        {
            if (flags.Count > 0 && s.TrajectoryBackwardFlags.Count > 0)
            {
                flags.AddRange(s.TrajectoryBackwardFlags.Skip(1));
            }
            else
            {
                flags.AddRange(s.TrajectoryBackwardFlags);
            }
        }
        return flags;
    }

    public ItinerarySegment? GetSegmentByPathId(string pathId)
    {
        return Segments.FirstOrDefault(s => s.PathId == pathId);
    }

    public IEnumerable<ItinerarySegment> GetRemainingSegments(int fromIndex)
    {
        return Segments.Skip(fromIndex);
    }
}
