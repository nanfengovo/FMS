declare namespace Fms {
  interface Point2D {
    x: number;
    y: number;
  }

  interface MapRasterMeta {
    available: boolean;
    width?: number;
    height?: number;
    originX?: number;
    topY?: number;
    resolution?: number;
    widthMeters?: number;
    heightMeters?: number;
    sourceName?: string;
  }

  interface Pose2D {
    x: number;
    y: number;
    theta: number;
  }

  type VehicleStatus =
    | 'Offline'
    | 'Idle'
    | 'Assigned'
    | 'Running'
    | 'WaitingLock'
    | 'Avoiding'
    | 'Charging'
    | 'Fault'
    | 'Estop'
    | 'Error'
    | 'Paused';

  interface VehicleDto {
    id: string;
    name: string;
    code?: string;
    vehicleType?: string;
    status: VehicleStatus;
    pose: Pose2D;
    currentSpeed: number;
    speed?: number;
    currentAngularVelocity?: number;
    batteryPercent: number;
    batteryVoltage: number;
    isCharging?: boolean;
    lowBatteryThreshold?: number;
    length?: number;
    width?: number;
    maxSpeed?: number;
    currentNodeId?: string | null;
    nearestNodeId?: string | null;
    targetNodeId?: string | null;
    homeNodeId?: string | null;
    currentOrderId?: string | null;
    assignedOrderId?: string | null;
    holdingPayload?: boolean;
    driveType: string;
    avoidance?: string;
    /** 车辆停下来在等谁：车辆编号，或互斥区等资源标识 */
    blockedBy?: string | null;
    isEnabled?: boolean;
    isOnline?: boolean;
    presenceOnline?: boolean;
    offlineReason?: 'disabled' | 'heartbeat_timeout' | string | null;
    lastHeartbeatUtc?: string | null;
    lastTelemetryUtc?: string | null;
    heartbeatTimeoutSec?: number;
    mapId?: string | null;
    mapName?: string | null;
    dispatchEligible?: boolean;
  }

  interface TaskStepItem {
    no: number;
    name: string;
    type: 'MODIFY_VEHICLE_PARAM' | 'Custom_Step' | 'SET_VAR' | 'MOVE' | 'CHECK_VAR' | string;
    status: 'Finish' | 'Ongoing to Run' | 'Waiting to Run' | 'Aborted';
    parameter: string;
  }

  interface OrderStepDto {
    stepNumber: number;
    nodeId: string;
    action: string;
    actionParameter?: string;
    isCompleted: boolean;
  }

  type OrderStatus =
    | 'Pending'
    | 'Queued'
    | 'Allocated'
    | 'Assigned'
    | 'Executing'
    | 'Running'
    | 'Completed'
    | 'Done'
    | 'Failed'
    | 'Canceled';

  interface OrderDto {
    orderId: string;
    serial?: string;
    assignedVehicleId: string | null;
    taskType?: string;
    sourceNodeId?: string | null;
    targetNodeId: string;
    status: OrderStatus;
    priority: number;
    createdAt?: string;
    steps?: OrderStepDto[];
    taskSteps?: TaskStepItem[];

    // Detailed industrial task properties (matching field controller screens)
    subNo?: string;
    taskCodeA?: string;
    taskCodeB?: string;
    taskVehicleNo?: number | string;
    finishingAction?: string;
    movementTimeSec?: number;
    waitTimeSec?: number;
    mileageKm?: number;
    completeTime?: string;
    armAction?: number;
    rfid?: string;
    succession?: number;
    cargoSerial?: string;
    goalAction?: number;
    mark?: number;
    optionCode?: string;
    passPoint?: string;
    passPointToGoal?: string;
    robotType?: number;
    storage?: string;
    target?: string;

    // Live snapshot fields pushed by the dispatch engine
    origin?: 'internal' | 'external' | 'system' | string;
    bindVehicleId?: string | null;
    currentStep?: number;
    totalSteps?: number;
    stepName?: string;
    stepAction?: string;
    stepTargetNodeId?: string;
    progress?: number;
    failReason?: string | null;
    startedAt?: string | null;
    completedAt?: string | null;
    /** Remaining route of the assigned vehicle as [x, y] world points (metres) */
    route?: number[][] | null;
  }

  interface ConflictAlertDto {
    timestamp: string;
    vehicle1Id: string;
    vehicle2Id: string;
    conflictType: string;
    conflictLocation: string;
    resolution: string;
  }

  interface MutexZoneDto {
    zoneId: string;
    name: string;
    memberNodeIds: string[];
    memberPathIds: string[];
    maxAllowedVehicles: number;
    currentOccupantVehicleIds: string[];
  }

  interface SimulationTelemetry {
    timestamp?: string;
    virtualTimeSeconds: number;
    timeMs?: number;
    tickIndex: number;
    timeScale: number;
    isPaused: boolean;
    vehicles: VehicleDto[];
    activeOrders?: OrderDto[];
    activeConflicts?: ConflictAlertDto[];
    mutexZones?: MutexZoneDto[];
  }

  type NodeType = 'Normal' | 'Park' | 'Charge' | 'Pick' | 'Drop' | 'Rest' | 'ChargePoint' | 'JobPoint' | 'PullOverPoint' | 'Landmark' | 'StandbyPoint' | string;

  interface MapNodeDto {
    id: string;
    name: string;
    x: number;
    y: number;
    type: NodeType;
    headingDeg?: number;
    allowRevolve?: boolean;
    spinDirection?: string;
    className?: string;
    dwellSec?: number;
  }

  type PathType = 'Straight' | 'CubicBezier' | 'Arc';

  interface MapPathDto {
    id: string;
    startNodeId: string;
    endNodeId: string;
    pathType: PathType;
    controlPoints: Point2D[];
    maxSpeed: number;
    length: number;
    isBidirectional: boolean;
    bidirectional?: boolean;
    direction?: string;
    roadWidth?: number;
    curveType?: string;
  }

  interface MutexZoneDto {
    zoneId: string;
    name: string;
    memberNodeIds: string[];
    memberPathIds: string[];
    maxAllowedVehicles: number;
    currentOccupantVehicleIds: string[];
    polygon?: Point2D[];
    timeoutSec?: number;
    associatedPathIds?: string[];
  }

  interface MapGraphDto {
    name: string;
    bounds?: { minX: number; maxX: number; minY: number; maxY: number };
    totalPointCloudCount?: number;
    pointCloudCount?: number;
    pointCloud?: Point2D[];
    nodes: MapNodeDto[];
    paths: MapPathDto[];
    mutexZones: MutexZoneDto[];
    lidarPoints?: Point2D[];
  }

  interface RcsTaskAddRequest {
    task_id?: string;
    task_serial?: string;
    robot_id?: string;
    task_type?: string;
    source_point?: string | number;
    target?: string | number;
    goal_action?: number;
    priority?: number;
    shelf_code?: string;
  }

  interface RcsProfileDto {
    id: string;
    name: string;
    description: string;
    vendor: string;
    rcsBaseUrl: string;
    useTypePrefixOnCallback: boolean;
    defaultTaskTypePrefix: string;
    optionCodeEnvelope: string;
    enableRfidCallback: boolean;
    enablePassBoxInterlock: boolean;
    enableMultiFloorElevator: boolean;
    pushEnabled: boolean;
    autoPermitInSimulation: boolean;
    autoRfidVerifiedInSimulation: boolean;
    cruiseSpeed: number;
    turningSpeed: number;
    creepSpeed: number;
    acceleration: number;
    deceleration: number;
    liftDurationSec: number;
    passBoxDwellSec: number;
    elevatorDwellSec: number;
    isActive: boolean;
  }


  interface LogEventEntry {
    id: string;
    timestamp: string;
    category: string;
    level: string;
    source: string;
    actor: string;
    message: string;
    payloadJson?: string | null;
    traceId?: string | null;
    method?: string | null;
    path?: string | null;
    statusCode?: number | null;
    durationMs?: number | null;
    success: boolean;
    taskSerial?: string | null;
    vehicleId?: string | null;
  }

  interface LogQueryParams {
    category?: string;
    source?: string;
    keyword?: string;
    level?: string;
    from?: string;
    to?: string;
    taskSerial?: string;
    vehicleId?: string;
    page?: number;
    pageSize?: number;
  }

  interface LogQueryResult {
    items: LogEventEntry[];
    total: number;
    lines: string[];
  }

  // 1. Logging & Flight Recorder
  interface AuditLogEntry {
    id: string;
    timestamp: string;
    operator: string;
    actionType: string;
    entityType: string;
    entityId: string;
    details: string;
    success: boolean;
  }

  interface FlightRecorderFrame {
    tickIndex: number;
    virtualTimeMs: number;
    vehicles: VehicleDto[];
    orders: OrderDto[];
    mutexZones: MutexZoneDto[];
    conflictAlerts: ConflictAlertDto[];
  }

  // 2. Configuration & Policies
  interface TrafficConfig {
    maxSpeedLimit: number;
    defaultTurningRadius: number;
    deadlockTimeoutSec: number;
    collisionSafetyRadiusMeters: number;
    highDensityThreshold: number;
    priorityWeights: Record<string, number>;
    emptyYieldLoaded?: boolean;
    defaultAvoidanceStrategy?: number | string;
    corridorSafetyBuffer?: number;
  }

  interface FmsConfigBundle {
    customSet: string;
    kcfmsXml: string;
    controlTags: Record<string, string>;
    trafficConfig: TrafficConfig;
    pushStats?: { totalPushed: number; lastPushedAt?: string | null };
  }

  // 3. RBAC & Identity
  interface UserDto {
    id: string;
    username: string;
    roles: string[];
    isActive: boolean;
  }

  interface RoleDto {
    name: string;
    description: string;
    permissions: string[];
  }

  interface AccessRole {
    roleName: string;
    description: string;
    permissions: string[];
  }

  interface AccessUser {
    id: string;
    username: string;
    isActive: boolean;
    roles: string[];
  }

  interface PermissionNode {
    name: string;
    parent: string | null;
    displayName: string;
    kind: number | string;
  }
}

