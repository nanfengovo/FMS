namespace Fms.Domain.Shared.Enums;

public enum VehicleStatus
{
    Idle = 0,
    Running = 1,
    Charging = 2,
    Paused = 3,
    Fault = 4,
    Offline = 5
}

public enum OrderStatus
{
    Queued = 0,
    Assigned = 1,
    Running = 2,
    WaitingAction = 3,
    Done = 4,
    Failed = 5,
    Cancelled = 6
}

/// <summary>
/// 工步状态。由订单状态与当前工步下标推导得出（单一来源），不单独存储，因此不会与订单状态脱节。
/// </summary>
public enum OrderStepStatus
{
    Pending = 0,
    Running = 1,
    Waiting = 2,
    Completed = 3,
    Skipped = 4,
    Cancelled = 5,
    Failed = 6
}

public enum NodeType
{
    Landmark = 0,
    JobPoint = 1,
    ChargePoint = 2,
    PullOverPoint = 3,
    StandbyPoint = 4
}

public enum PathType
{
    StraightLine = 0,
    CubicBezier = 1,
    Arc = 2
}

public enum ActionType
{
    Move = 0,
    Wait = 1,
    LiftUp = 2,
    PutDown = 3,
    Charge = 4,
    CheckRfid = 5,
    Spin = 6,
    ClaimMutex = 7,
    ReleaseMutex = 8,
    DecisionBranch = 9,
    WaitIoSignal = 10,
    Custom = 99
}

public enum ClockMode
{
    RealtimeTick = 0,
    DesFastForward = 1,
    StepMode = 2
}

public enum AvoidanceStrategy
{
    None = 0,
    WaitInPlace = 1,
    PullOverSpur = 2,
    DynamicReroute = 3
}

public enum DriveType
{
    Differential = 0,  // 双轮差速
    SingleSteer = 1,   // 单舵轮
    DualSteer = 2,     // 双舵轮
    Mecanum = 3        // 麦克纳姆轮 (全向)
}

public enum SegmentMotionMode
{
    Forward = 0,       // 沿切线车头朝前行驶
    Backward = 1,      // 倒车驶入工位/路段
    LateralShift = 2,  // 双差速横移 / 麦轮全向平移
    SpinInPlace = 3    // 原地自旋调头
}

public enum ResourceType
{
    PathEdge = 0,      // 几何路段
    MutexZone = 1,     // 多边形交管互斥区
    StationNode = 2    // 站点道口 / 窄道喉部
}

public enum StorageSlotStatus
{
    Empty = 0,         // 空闲
    Reserved = 1,      // 预约中 (待取或待放)
    Occupied = 2,      // 占用有货
    Locked = 3         // 锁定 (维护/故障)
}

public enum StorageZoneType
{
    Raw = 0,           // 原料区 (只出不进)
    Wip = 1,           // 线边缓存区 (有容量上限)
    Finished = 2,      // 成品区 (只进不出)
    Charge = 3,        // 充电桩区 (单桩一车)
    Park = 4           // 待命停靠区
}

public enum DecisionCategory
{
    Routing = 0,          // 路径搜索与重规划
    ResourceLease = 1,    // 资源租约申请与互斥
    TrafficAvoidance = 2, // 对向避让与道口等待
    OrderDispatch = 3,    // 工单调度与选车
    WorkflowBranch = 4,   // 流程实例条件分支与信号
    SafetyInterlock = 5,  // 安全互锁保护
    DeadlockResolution = 6,// 死锁解除与强制脱困
    StorageAllocation = 7 // 库区与货位存取分配
}

