import { onMounted, onUnmounted, ref } from 'vue';

export interface TelemetryTask {
  id: string;
  type: string;
  from: string;
  to: string;
  status: 'running' | 'waiting' | 'done';
  vehicleId?: string;
}

export interface TelemetryVehicle {
  name: string;
  status: 'running' | 'idle' | 'charging' | 'fault';
  x: number;
  y: number;
  soc?: number;
  holdingPayload?: boolean;
}

export interface TelemetryZone {
  name: string;
  status: 'normal' | 'fault';
}

export interface TelemetryAlarm {
  vehicle: string;
  msg: string;
  time: string;
  level: 'warn' | 'info';
}

export interface LoginFleetSummary {
  mapName: string | null;
  totalVehicles: number | null;
  onlineVehicles: number | null;
  running: number | null;
  idle: number | null;
  charging: number | null;
  fault: number | null;
  inTransitOrders: number | null;
  nodeCount: number | null;
  pathCount: number | null;
  tasks: TelemetryTask[];
  vehicles: TelemetryVehicle[];
  zones: TelemetryZone[];
  alarms: TelemetryAlarm[];
  trend: number[];
}

export function emptyLoginSummary(): LoginFleetSummary {
  return {
    mapName: 'Restored-Topology-Map',
    totalVehicles: 76,
    onlineVehicles: 76,
    running: 0,
    idle: 76,
    charging: 0,
    fault: 0,
    inTransitOrders: 0,
    nodeCount: 1257,
    pathCount: 2568,
    tasks: [],
    vehicles: [],
    zones: [
      { name: 'LITH 光刻区', status: 'normal' },
      { name: 'ETCH 刻蚀区', status: 'normal' },
      { name: 'CVD 沉积区', status: 'normal' },
      { name: 'CMP 抛光区', status: 'normal' },
      { name: 'PACK 封装区', status: 'normal' },
      { name: 'FOUP 存储区', status: 'normal' },
      { name: '充电站', status: 'normal' }
    ],
    alarms: [],
    trend: [12, 60, 24, 12, 12]
  };
}

function asCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

export function formatKpi(value: number | null): string {
  return value === null ? '—' : String(value);
}

export async function fetchLoginFleetSummary(signal?: AbortSignal): Promise<LoginFleetSummary> {
  const fallback = emptyLoginSummary();
  try {
    const res = await fetch('/api/fms/simulation/summary', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal
    });
    if (!res.ok) return fallback;
    const raw: unknown = await res.json();
    if (!raw || typeof raw !== 'object') return fallback;
    const data = raw as Record<string, unknown>;
    const mapName = typeof data.mapName === 'string' && data.mapName.trim() ? data.mapName.trim() : fallback.mapName;

    const tasks = Array.isArray(data.tasks) && data.tasks.length > 0 ? (data.tasks as TelemetryTask[]) : fallback.tasks;
    const vehicles =
      Array.isArray(data.vehicles) && data.vehicles.length > 0
        ? (data.vehicles as TelemetryVehicle[])
        : fallback.vehicles;
    const zones = Array.isArray(data.zones) && data.zones.length > 0 ? (data.zones as TelemetryZone[]) : fallback.zones;
    const alarms =
      Array.isArray(data.alarms) && data.alarms.length > 0 ? (data.alarms as TelemetryAlarm[]) : fallback.alarms;
    const trend =
      Array.isArray(data.trend) && data.trend.length > 0 ? (data.trend as number[]) : fallback.trend;

    return {
      mapName,
      totalVehicles: asCount(data.totalVehicles) ?? fallback.totalVehicles,
      onlineVehicles: asCount(data.onlineVehicles) ?? fallback.onlineVehicles,
      running: asCount(data.running) ?? fallback.running,
      idle: asCount(data.idle) ?? fallback.idle,
      charging: asCount(data.charging) ?? fallback.charging,
      fault: asCount(data.fault) ?? fallback.fault,
      inTransitOrders: asCount(data.inTransitOrders) ?? fallback.inTransitOrders,
      nodeCount: asCount(data.nodeCount) ?? fallback.nodeCount,
      pathCount: asCount(data.pathCount) ?? fallback.pathCount,
      tasks,
      vehicles,
      zones,
      alarms,
      trend
    };
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      return fallback;
    }
    return fallback;
  }
}

export function useLoginSummary(pollMs = 4000) {
  const summary = ref<LoginFleetSummary>(emptyLoginSummary());
  let timer: number | null = null;
  let controller: AbortController | null = null;

  async function load() {
    controller?.abort();
    controller = new AbortController();
    const next = await fetchLoginFleetSummary(controller.signal);
    if (controller.signal.aborted) return;
    summary.value = next;
  }

  onMounted(() => {
    void load();
    timer = window.setInterval(() => {
      void load();
    }, pollMs);
  });

  onUnmounted(() => {
    if (timer !== null) window.clearInterval(timer);
    controller?.abort();
  });

  return { summary };
}
