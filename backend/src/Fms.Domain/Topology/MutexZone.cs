using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Topology;

public class MutexZone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxCapacity { get; set; } = 1;
    public double TimeoutSec { get; set; } = 60.0;
    public List<Point2D> Polygon { get; set; } = new();
    public HashSet<string> AssociatedNodeIds { get; set; } = new();
    public HashSet<string> AssociatedPathIds { get; set; } = new();
    
    public HashSet<string> ActiveClaimantVehicleIds { get; set; } = new();
    public string? CurrentClaimantVehicleId => ActiveClaimantVehicleIds.FirstOrDefault();
    public DateTime? FirstClaimedAt { get; set; }

    public bool IsOccupied => ActiveClaimantVehicleIds.Count > 0;
    public bool IsFull => ActiveClaimantVehicleIds.Count >= MaxCapacity;

    public MutexZone() { }

    public MutexZone(string id, string name, int maxCapacity = 1, double timeoutSec = 60.0)
    {
        Id = id;
        Name = name;
        MaxCapacity = maxCapacity;
        TimeoutSec = timeoutSec;
    }

    public static bool IsPointInPolygon(Point2D pt, IReadOnlyList<Point2D> polygon)
    {
        if (polygon == null || polygon.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            bool intersect = ((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                             (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y + 1e-9) + pi.X);

            if (intersect) inside = !inside;
        }

        return inside;
    }

    public bool ContainsPoint(Point2D pt) => IsPointInPolygon(pt, Polygon);

    public bool TryClaim(string vehicleId, DateTime? currentTimeUtc = null)
    {
        var now = currentTimeUtc ?? DateTime.UtcNow;
        lock (ActiveClaimantVehicleIds)
        {
            if (ActiveClaimantVehicleIds.Contains(vehicleId)) return true;

            if (ActiveClaimantVehicleIds.Count < MaxCapacity)
            {
                ActiveClaimantVehicleIds.Add(vehicleId);
                FirstClaimedAt ??= now;
                return true;
            }

            // 超时仅产生告警状态，绝不强制抢占拆除工业安全锁
            if (FirstClaimedAt.HasValue && (now - FirstClaimedAt.Value).TotalSeconds > TimeoutSec)
            {
                HasTimeoutAlarm = true;
            }

            return false;
        }
    }

    public bool HasTimeoutAlarm { get; set; } = false;

    public void Release(string vehicleId)
    {
        lock (ActiveClaimantVehicleIds)
        {
            ActiveClaimantVehicleIds.Remove(vehicleId);
            if (ActiveClaimantVehicleIds.Count == 0)
            {
                FirstClaimedAt = null;
            }
        }
    }

    public void ForceRelease()
    {
        lock (ActiveClaimantVehicleIds)
        {
            ActiveClaimantVehicleIds.Clear();
            FirstClaimedAt = null;
        }
    }
}
