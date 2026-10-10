export interface Point2D {
  x: number;
  y: number;
}

export interface Pose2D {
  x: number;
  y: number;
  theta: number;
}

export type VehicleStatus =
  | 'Offline'
  | 'Idle'
  | 'Assigned'
  | 'Running'
  | 'WaitingLock'
  | 'Avoiding'
  | 'Charging'
  | 'Error'
  | 'Paused';

export interface VehicleDto {
  id: string;
  name: string;
  status: VehicleStatus;
  pose: Pose2D;
  currentSpeed: number;
  currentAngularVelocity: number;
  batteryPercent: number;
  batteryVoltage: number;
  isCharging: boolean;
  currentNodeId: string | null;
  targetNodeId: string | null;
  currentOrderId: string | null;
  holdingPayload: boolean;
  driveType: string;
}

export interface OrderStepDto {
  stepNumber: number;
  nodeId: string;
  action: string;
  actionParameter: string;
  isCompleted: boolean;
}

export type OrderStatus =
  | 'Pending'
  | 'Allocated'
  | 'Executing'
  | 'Completed'
  | 'Failed'
  | 'Canceled';

export interface OrderDto {
  orderId: string;
  assignedVehicleId: string | null;
  taskType: string;
  sourceNodeId: string | null;
  targetNodeId: string;
  status: OrderStatus;
  priority: number;
  createdAt: string;
  steps: OrderStepDto[];
}

export interface ConflictAlertDto {
  timestamp: string;
  vehicle1Id: string;
  vehicle2Id: string;
  conflictType: string;
  conflictLocation: string;
  resolution: string;
}

export interface MutexZoneDto {
  zoneId: string;
  name: string;
  memberNodeIds: string[];
  memberPathIds: string[];
  maxAllowedVehicles: number;
  currentOccupantVehicleIds: string[];
}

export interface SimulationTelemetry {
  timestamp: string;
  virtualTimeSeconds: number;
  tickIndex: number;
  timeScale: number;
  isPaused: boolean;
  vehicles: VehicleDto[];
  activeOrders: OrderDto[];
  activeConflicts: ConflictAlertDto[];
  mutexZones: MutexZoneDto[];
}

export type NodeType = 'Normal' | 'Park' | 'Charge' | 'Pick' | 'Drop' | 'Rest';

export interface MapNodeDto {
  id: string;
  name: string;
  x: number;
  y: number;
  type: NodeType;
}

export type PathType = 'Straight' | 'CubicBezier' | 'Arc';

export interface MapPathDto {
  id: string;
  startNodeId: string;
  endNodeId: string;
  pathType: PathType;
  controlPoints: Point2D[];
  maxSpeed: number;
  length: number;
  isBidirectional: boolean;
}

export interface MapGraphDto {
  name: string;
  nodes: MapNodeDto[];
  paths: MapPathDto[];
  mutexZones: MutexZoneDto[];
  lidarPoints?: Point2D[];
}

export interface RcsTaskAddRequest {
  task_id: string;
  robot_id?: string;
  task_type: string;
  source_point?: string | number;
  target: string | number;
  goal_action?: number; // 0-none, 1-take, 2-put
  priority?: number;
  shelf_code?: string;
}

export interface RcsProfileDto {
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

