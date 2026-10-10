namespace Fms.Domain.Topology;

/// <summary>
/// 线路视图：一组拓扑边的逻辑集合，用于调度员视图、车型准入过滤与批量限速/封路
/// </summary>
public class RouteLine
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> MemberPathIds { get; set; } = new();

    public List<string> AllowedVehicleTypes { get; set; } = new() { "ALL" };
    public string DefaultDirection { get; set; } = "forward";
    public bool AllowBackward { get; set; } = false;
    public double SpeedLimit { get; set; } = 1.5;
    public bool IsClosed { get; set; } = false;
    public string? ClosureReason { get; set; }

    public RouteLine() { }

    public RouteLine(string id, string name, IEnumerable<string> pathIds, double speedLimit = 1.5)
    {
        Id = id;
        Name = name;
        MemberPathIds = pathIds.ToList();
        SpeedLimit = speedLimit;
    }
}
