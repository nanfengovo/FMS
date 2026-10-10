using Fms.Domain.Shared.Enums;
using Fms.Domain.Shared.Geometry;

namespace Fms.Domain.Vehicles;

public class VehicleAggregate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string VehicleType { get; set; } = "LATENT_AMR";
    public string Group { get; set; } = "ALL";
    public VehicleStatus Status { get; set; } = VehicleStatus.Idle;
    public bool IsOnline { get; set; } = true;
    /// <summary>台账启用。停用后不再承接新单，在跑订单按现有语义跑完。</summary>
    public bool IsEnabled { get; set; } = true;
    public bool IsEstop { get; set; } = false;
    public bool IsPaused { get; set; } = false;
    public bool AutoSimulateHeartbeat { get; set; } = true;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

    /// <summary>绑定的地图标识（换图后仍保留，便于台账回看所属图）。</summary>
    public string MapId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    /// <summary>停靠/归属点，来自当前地图 StandbyPoint 或 ChargePoint。</summary>
    public string? HomeNodeId { get; set; }

    public Pose2D Pose { get; set; } = Pose2D.Zero;
    public double CurrentSpeed { get; set; } = 0.0;
    public double CurrentAngularVelocity { get; set; } = 0.0;

    public VehicleKinematics Kinematics { get; set; } = new();
    public BatteryBms Bms { get; set; } = new();
    public bool HoldingPayload { get; set; } = false;
    public string? PayloadCarrierId { get; set; }
    public double LiftProgress { get; set; } = 0.0; // 0.0 (下降) ~ 1.0 (举升)
    public string ActionPhase { get; set; } = "Idle"; // Idle, Moving, Arrived, PermitWait, LiftingUp, PuttingDown, CheckingRfid, Charging
    public string CargoType { get; set; } = "FOUP"; // FOUP (晶圆盒), Magazine (料盒), Tray (托盘), Carrier

    // 导航与路径上下文
    public string? CurrentNodeId { get; set; }
    public string? TargetNodeId { get; set; }
    public string? CurrentPathId { get; set; }
    public string? AssignedOrderId { get; set; }

    // 规划出的路径段与离散采样轨迹
    public List<string> RoutePathIds { get; set; } = new();
    public List<Point2D> TrajectoryPoints { get; set; } = new();
    public List<bool> TrajectoryBackwardFlags { get; set; } = new();
    public bool IsMovingBackward { get; set; } = false;
    public int CurrentTrajectoryIndex { get; set; } = 0;

    /// <summary>车辆正从路网外驶回路网入口（到达首个轨迹点前不做横向偏差评估）</summary>
    public bool IsRejoining { get; set; } = false;

    // 资源预约与独占持有
    public HashSet<string> ReservedPathIds { get; set; } = new();
    public string? ClaimedMutexZoneId { get; set; }

    // 冲突与避让
    public string? BlockedByVehicleId { get; set; }
    public AvoidanceStrategy CurrentAvoidanceStrategy { get; set; } = AvoidanceStrategy.None;
    public string? AvoidanceTargetStationId { get; set; }
    public DateTime? WaitUntilTime { get; set; }

    // 避让让行目标保护上下文 (Yield & Resume)
    public bool IsYielding { get; set; } = false;
    public string? SuspendedOrderId { get; set; }
    public string? SuspendedTargetNodeId { get; set; }
    public List<string> SuspendedRoutePathIds { get; set; } = new();

    // 跟驰模型上下文 (Car-Following)
    public string? CarFollowingTargetVehicleId { get; set; }
    public double? CarFollowingTargetSpeed { get; set; }

    /// <summary>被激光防撞红线连续拦停的累计时间（仿真秒），用于识别互相拦停的死锁。</summary>
    public double BumperStallSeconds { get; set; }

    /// <summary>死锁破除：在该时刻（仿真时钟）之前，本车对同样静止的车辆放行。</summary>
    public DateTime? BumperOverrideUntil { get; set; }

    public VehicleAggregate() { }

    public VehicleAggregate(string id, string name, Pose2D initPose)
    {
        Id = id;
        Name = name;
        Code = id;
        Pose = initPose;
    }

    // 结构化时空行程与导航跟踪
    public Fms.Domain.Traffic.Itinerary? CurrentItinerary { get; set; }
    public Fms.Domain.Traffic.NavigationTrackingReport? LastNavigationReport { get; set; }
    public Fms.Domain.Adapters.IVehicleCommAdapter? CommAdapter { get; set; }

    public void AssignRoute(string orderId, List<string> pathIds, List<Point2D> trajectory, string targetNodeId, List<bool>? backwardFlags = null, Fms.Domain.Traffic.Itinerary? itinerary = null)
    {
        AssignedOrderId = orderId;
        RoutePathIds = pathIds;
        TrajectoryPoints = trajectory;
        TrajectoryBackwardFlags = backwardFlags ?? new List<bool>();
        CurrentTrajectoryIndex = 0;
        IsRejoining = false;
        TargetNodeId = targetNodeId;
        Status = VehicleStatus.Running;
        BlockedByVehicleId = null;
        CarFollowingTargetVehicleId = null;
        CarFollowingTargetSpeed = null;
        BumperStallSeconds = 0.0;
        IsMovingBackward = TrajectoryBackwardFlags.FirstOrDefault();
        CurrentItinerary = itinerary;
        ActionPhase = "Moving";
    }

    public void SuspendCurrentRouteForAvoidance(string spurStationId, List<string> spurPathIds, List<Point2D> spurTrajectory, List<bool>? spurBackwardFlags = null)
    {
        // 暂存原订单目标与剩余路线
        IsYielding = true;
        SuspendedOrderId = AssignedOrderId;
        SuspendedTargetNodeId = TargetNodeId;
        SuspendedRoutePathIds = new List<string>(RoutePathIds);

        // 指向避让支线
        CurrentAvoidanceStrategy = AvoidanceStrategy.PullOverSpur;
        AvoidanceTargetStationId = spurStationId;
        AssignRoute(AssignedOrderId ?? "Avoidance", spurPathIds, spurTrajectory, spurStationId, spurBackwardFlags);
    }

    public void ClearRoute()
    {
        RoutePathIds.Clear();
        TrajectoryPoints.Clear();
        TrajectoryBackwardFlags.Clear();
        CurrentTrajectoryIndex = 0;
        IsRejoining = false;
        TargetNodeId = null;
        CurrentSpeed = 0.0;
        Status = VehicleStatus.Idle;
        ActionPhase = "Idle";
        BlockedByVehicleId = null;
        CurrentAvoidanceStrategy = AvoidanceStrategy.None;
        AvoidanceTargetStationId = null;
        CarFollowingTargetVehicleId = null;
        CarFollowingTargetSpeed = null;
        BumperStallSeconds = 0.0;
        BumperOverrideUntil = null;
        IsYielding = false;
        IsMovingBackward = false;
        SuspendedOrderId = null;
        SuspendedTargetNodeId = null;
        SuspendedRoutePathIds.Clear();
    }
}
