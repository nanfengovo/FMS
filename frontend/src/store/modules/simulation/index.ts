import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { fmsApi } from '@/service/api/fms';
import { signalRService } from '@/service/socket/signalr';

export const useSimulationStore = defineStore('simulation', () => {
  const isPaused = ref(false);
  const timeScale = ref(1.0);
  const virtualTimeSeconds = ref(0);
  const tickIndex = ref(0);
  const vehicles = ref<Fms.VehicleDto[]>([]);
  const activeOrders = ref<Fms.OrderDto[]>([]);
  const activeConflicts = ref<Fms.ConflictAlertDto[]>([]);
  const mutexZones = ref<Fms.MutexZoneDto[]>([]);
  const conflictHistory = ref<Fms.ConflictAlertDto[]>([]);
  const selectedVehicleId = ref<string | null>(null);

  const selectedVehicle = computed(() => {
    if (!selectedVehicleId.value) return null;
    return vehicles.value.find(v => v.id === selectedVehicleId.value) || null;
  });

  const fleetStats = computed(() => {
    const total = vehicles.value.length;
    const running = vehicles.value.filter(v => v.status === 'Running').length;
    const idle = vehicles.value.filter(v => v.status === 'Idle').length;
    const avoiding = vehicles.value.filter(v => v.status === 'Avoiding').length;
    const waiting = vehicles.value.filter(v => v.status === 'WaitingLock').length;
    const error = vehicles.value.filter(v => v.status === 'Error').length;
    const charging = vehicles.value.filter(v => v.status === 'Charging').length;
    return { total, running, idle, avoiding, waiting, error, charging };
  });

  function mapVehicle(v: any, prev?: Fms.VehicleDto): Fms.VehicleDto {
    const thetaRaw = v.pose?.theta ?? v.theta;
    const theta =
      thetaRaw === undefined
        ? (prev?.pose.theta ?? 0)
        : Math.abs(thetaRaw) > 2 * Math.PI
          ? (thetaRaw * Math.PI) / 180
          : thetaRaw;

    return {
      id: v.id,
      name: v.name || prev?.name || v.id,
      code: v.code || prev?.code || v.id,
      vehicleType: v.vehicleType || prev?.vehicleType,
      status: v.status || prev?.status || 'Idle',
      pose: {
        x: v.pose?.x ?? v.x ?? prev?.pose.x ?? 0,
        y: v.pose?.y ?? v.y ?? prev?.pose.y ?? 0,
        theta
      },
      speed: v.currentSpeed ?? v.speed ?? prev?.speed ?? 0,
      currentSpeed: v.currentSpeed ?? v.speed ?? prev?.currentSpeed ?? 0,
      currentAngularVelocity: v.currentAngularVelocity ?? prev?.currentAngularVelocity,
      batteryPercent: v.batteryPercent ?? v.battery ?? prev?.batteryPercent ?? 100,
      batteryVoltage: v.batteryVoltage ?? prev?.batteryVoltage ?? 48.0,
      isCharging: v.isCharging ?? prev?.isCharging ?? false,
      lowBatteryThreshold: v.lowBatteryThreshold ?? prev?.lowBatteryThreshold ?? 20,
      length: v.length ?? prev?.length ?? 1.2,
      width: v.width ?? prev?.width ?? 0.8,
      maxSpeed: v.maxSpeed ?? prev?.maxSpeed,
      currentNodeId: v.currentNodeId ?? v.nodeId ?? prev?.currentNodeId ?? null,
      nearestNodeId: v.nearestNodeId !== undefined ? v.nearestNodeId : (prev?.nearestNodeId ?? null),
      targetNodeId: v.targetNodeId !== undefined ? v.targetNodeId : (prev?.targetNodeId ?? null),
      homeNodeId: v.homeNodeId !== undefined ? v.homeNodeId : (prev?.homeNodeId ?? null),
      currentOrderId:
        v.assignedOrderId !== undefined || v.currentOrderId !== undefined || v.orderId !== undefined
          ? (v.assignedOrderId ?? v.currentOrderId ?? v.orderId ?? null)
          : (prev?.currentOrderId ?? null),
      assignedOrderId:
        v.assignedOrderId !== undefined || v.currentOrderId !== undefined
          ? (v.assignedOrderId ?? v.currentOrderId ?? null)
          : (prev?.assignedOrderId ?? null),
      holdingPayload: v.holdingPayload ?? v.payload ?? prev?.holdingPayload ?? false,
      driveType: v.driveType ?? prev?.driveType ?? 'Differential',
      avoidance: v.avoidance ?? prev?.avoidance ?? 'None',
      blockedBy: v.blockedBy !== undefined ? v.blockedBy : (prev?.blockedBy ?? null),
      isEnabled: v.isEnabled !== undefined ? v.isEnabled : (prev?.isEnabled ?? true),
      isOnline: v.isOnline !== undefined ? v.isOnline : (prev?.isOnline ?? true),
      presenceOnline: v.presenceOnline !== undefined ? v.presenceOnline : (prev?.presenceOnline ?? true),
      offlineReason: v.offlineReason !== undefined ? v.offlineReason : (prev?.offlineReason ?? null),
      lastHeartbeatUtc: v.lastHeartbeatUtc ?? v.lastTelemetryUtc ?? prev?.lastHeartbeatUtc ?? null,
      lastTelemetryUtc: v.lastTelemetryUtc ?? v.lastHeartbeatUtc ?? prev?.lastTelemetryUtc ?? null,
      heartbeatTimeoutSec: v.heartbeatTimeoutSec ?? prev?.heartbeatTimeoutSec ?? 5,
      mapId: v.mapId !== undefined ? v.mapId : (prev?.mapId ?? null),
      mapName: v.mapName !== undefined ? v.mapName : (prev?.mapName ?? null),
      dispatchEligible: v.dispatchEligible !== undefined ? v.dispatchEligible : prev?.dispatchEligible
    };
  }

  function applyTelemetry(data: any) {
    if (data.virtualTimeSeconds !== undefined) virtualTimeSeconds.value = data.virtualTimeSeconds;
    else if (data.timeMs !== undefined) virtualTimeSeconds.value = data.timeMs / 1000.0;
    if (data.tickIndex !== undefined) tickIndex.value = data.tickIndex;
    if (data.isPaused !== undefined) isPaused.value = data.isPaused;
    if (data.timeScale !== undefined) timeScale.value = data.timeScale;
    if (data.vehicles) {
      const prev = new Map(vehicles.value.map(item => [item.id, item]));
      vehicles.value = data.vehicles.map((v: any) => mapVehicle(v, prev.get(v.id)));
    }
    if (data.activeOrders && data.activeOrders.length > 0) applyOrderSnapshot(data.activeOrders);
    else if (data.orders && !data.activeOrders) {
      activeOrders.value = data.orders.map((o: any) => ({
        orderId: o.id || o.orderId,
        serial: o.serial,
        status: o.status,
        priority: o.priority,
        assignedVehicleId: o.assignedVehicle,
        sourceNodeId: o.sourceNodeId || '',
        targetNodeId: o.targetNodeId && o.targetNodeId !== 'DEST' ? o.targetNodeId : '',
        steps: []
      }));
    }
    if (data.activeConflicts) activeConflicts.value = data.activeConflicts;
    if (data.mutexZones) mutexZones.value = data.mutexZones;
  }

  function handleConflictAlert(alert: Fms.ConflictAlertDto) {
    conflictHistory.value.unshift(alert);
    if (conflictHistory.value.length > 50) {
      conflictHistory.value.pop();
    }
  }

  let cleanupTelemetry: (() => void) | null = null;
  let cleanupConflict: (() => void) | null = null;
  let cleanupOrders: (() => void) | null = null;

  /** 任务快照：引擎每 500ms 推送一次，包含来源、起止站点、进度和剩余路线 */
  function applyOrderSnapshot(orders: any[]) {
    activeOrders.value = (orders || []).map((o: any) => ({ ...o, status: o.status as Fms.OrderStatus }));
  }

  const liveOrders = computed(() =>
    activeOrders.value.filter(o => !['Done', 'Failed', 'Cancelled', 'Canceled', 'Completed'].includes(String(o.status)))
  );

  async function init() {
    try {
      const state = await fmsApi.getSimulationState();
      applyTelemetry(state);
    } catch (e) {
      console.warn('Initial telemetry pull failed:', e);
    }

    cleanupTelemetry?.();
    cleanupConflict?.();
    cleanupOrders?.();
    cleanupTelemetry = signalRService.onTelemetry(applyTelemetry);
    cleanupConflict = signalRService.onConflict(handleConflictAlert);
    cleanupOrders = signalRService.onOrders(applyOrderSnapshot);
    ensureFallbackPolling();
    await signalRService.start();
  }

  /** 实时通道未连上时（令牌过期、网络抖动）退回到 1.5s 轮询，保证车辆和任务仍然在动。 */
  let fallbackTimer: number | undefined;

  function ensureFallbackPolling() {
    if (fallbackTimer) return;
    fallbackTimer = window.setInterval(() => {
      if (signalRService.isConnected.value) return;
      pollSimulationState();
    }, 1500);
  }

  async function pollSimulationState() {
    try {
      const state = await fmsApi.getSimulationState();
      applyTelemetry(state);
      return state;
    } catch (e) {
      console.warn('Telemetry poll failed:', e);
    }
  }

  const currentTimestampMs = computed(() => (virtualTimeSeconds.value || 0) * 1000);

  async function togglePause() {
    if (isPaused.value) {
      await fmsApi.resumeSimulation();
      isPaused.value = false;
    } else {
      await fmsApi.pauseSimulation();
      isPaused.value = true;
    }
  }

  async function pauseSimulation() {
    await fmsApi.pauseSimulation();
    isPaused.value = true;
  }

  async function resumeSimulation() {
    await fmsApi.resumeSimulation();
    isPaused.value = false;
  }

  async function setSpeed(scale: number) {
    await fmsApi.setTimeScale(scale);
    timeScale.value = scale;
  }

  async function setTimeScale(scale: number) {
    await setSpeed(scale);
  }

  async function step() {
    await fmsApi.stepSimulation();
  }

  async function stepSimulation(_delta = 0.1) {
    await fmsApi.stepSimulation();
  }

  async function injectFault(
    vehicleId: string,
    faultType: 'Estop' | 'Pause' | 'LowBattery' | 'Alarm' | 'ClearFault'
  ) {
    await fmsApi.injectFault({ vehicleId, faultType });
  }

  async function spawnVehicle(params: {
    id: string;
    name?: string;
    initialNodeId?: string;
    driveType?: string;
  }) {
    await fmsApi.spawnVehicle(params);
  }

  async function createOrder(targetNodeId: string) {
    await fmsApi.addRcsTask({
      task_id: `T-${Date.now()}`,
      task_type: 'MOVE',
      target: targetNodeId
    });
  }

  /** 手动拉一次最新任务（SignalR 尚未连上或刚下单时使用） */
  async function refreshOrders() {
    const state = await fmsApi.getSimulationState();
    if (state?.activeOrders) applyOrderSnapshot(state.activeOrders);
  }

  return {
    isPaused,
    timeScale,
    virtualTimeSeconds,
    tickIndex,
    vehicles,
    activeOrders,
    liveOrders,
    applyOrderSnapshot,
    activeConflicts,
    mutexZones,
    conflictHistory,
    selectedVehicleId,
    selectedVehicle,
    fleetStats,
    applyTelemetry,
    init,
    currentTimestampMs,
    pollSimulationState,
    togglePause,
    pauseSimulation,
    resumeSimulation,
    setTimeScale,
    stepSimulation,
    setSpeed,
    step,
    injectFault,
    spawnVehicle,
    createOrder,
    refreshOrders
  };
});
