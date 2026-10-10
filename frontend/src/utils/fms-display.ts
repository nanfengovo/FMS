const orderStatusLabels: Record<string, string> = {
  pending: '待处理',
  queued: '排队中',
  allocated: '已分配',
  assigned: '执行中',
  executing: '执行中',
  running: '行驶中',
  waitingaction: '等待动作',
  paused: '已暂停',
  completed: '已完结',
  done: '已完结',
  failed: '调度失败',
  canceled: '已取消',
  cancelled: '已取消',
  fault: '故障',
  terminated: '已终止'
};

const vehicleStatusLabels: Record<string, string> = {
  idle: '待命',
  running: '运行',
  paused: '暂停',
  charging: '充电',
  error: '故障',
  fault: '故障',
  estop: '急停',
  avoiding: '避让',
  docking: '停靠',
  waitinglock: '等锁',
  offline: '离线'
};

const driveTypeLabels: Record<string, string> = {
  differential: '差速',
  singlesteer: '单舵轮',
  dualsteer: '双舵轮',
  mecanum: '全向'
};

export function driveTypeLabel(driveType?: string | number | null) {
  if (driveType === undefined || driveType === null || driveType === '') return '—';
  return driveTypeLabels[String(driveType).toLowerCase()] || String(driveType);
}

const slotStatusLabels: Record<string, string> = {
  empty: '空闲',
  occupied: '占用',
  reserved: '预约',
  locked: '锁定'
};

function labelOf(table: Record<string, string>, status?: string | null) {
  if (!status) return '—';
  return table[status.toLowerCase()] || status;
}

export function orderStatusLabel(status?: string | null) {
  return labelOf(orderStatusLabels, status);
}

export function vehicleStatusLabel(status?: string | null) {
  return labelOf(vehicleStatusLabels, status);
}

export function slotStatusLabel(status?: string | null) {
  return labelOf(slotStatusLabels, status);
}

const orderTypeLabels: Record<string, string> = {
  transport: '点到点搬运',
  workflow: '流程任务',
  nav: '单点导航',
  navigation: '单点导航',
  charge: '指定回充',
  task: '调度任务'
};

const logLevelLabels: Record<string, string> = {
  trace: '跟踪',
  debug: '调试',
  information: '信息',
  info: '信息',
  warning: '警告',
  warn: '警告',
  error: '错误',
  critical: '严重',
  fatal: '致命'
};

const logSourceLabels: Record<string, string> = {
  inbound: '入站',
  outbound: '出站',
  api: '接口',
  useroperation: '用户操作',
  runtime: '运行',
  audit: '审计'
};

export function orderTypeLabel(type?: string | null) {
  return labelOf(orderTypeLabels, type);
}

const taskSourceLabels: Record<string, string> = {
  internal: '内部创建',
  task: '外部接口',
  mes: 'MES 系统',
  wms: 'WMS 系统',
  auto: '系统自动',
  workflow: '流程编排'
};

export function taskSourceLabel(source?: string | null) {
  const key = String(source ?? '').toLowerCase();
  if (taskSourceLabels[key]) return taskSourceLabels[key];
  const hit = Object.keys(taskSourceLabels).find(k => key.includes(k));
  return hit ? taskSourceLabels[hit] : labelOf(orderTypeLabels, source);
}

export function orderOriginLabel(origin?: string | null) {
  return origin === 'internal' ? '内部创建' : origin === 'system' ? '系统自动' : origin === 'external' ? '外部接口' : '—';
}

export function logLevelLabel(level?: string | null) {
  return labelOf(logLevelLabels, level);
}

export function logSourceLabel(source?: string | null) {
  return labelOf(logSourceLabels, source);
}

export function logOriginLabel(category?: string | null, source?: string | null) {
  if ((source || '').toLowerCase() === 'audit') return logSourceLabel('audit');
  const fromCategory = logSourceLabel(category);
  if (fromCategory && fromCategory !== '—') return fromCategory;
  return logSourceLabel(source);
}

export function logOriginTag(category?: string | null, source?: string | null): 'default' | 'info' | 'success' | 'warning' | 'error' {
  const key = ((source || '').toLowerCase() === 'audit' ? 'audit' : (category || source || '')).toLowerCase();
  switch (key) {
    case 'inbound':
      return 'info';
    case 'outbound':
      return 'success';
    case 'api':
      return 'default';
    case 'useroperation':
    case 'audit':
      return 'warning';
    case 'runtime':
      return 'info';
    default:
      return 'default';
  }
}

export function logLevelTag(level?: string | null): 'default' | 'info' | 'success' | 'warning' | 'error' {
  switch ((level || '').toLowerCase()) {
    case 'warning':
    case 'warn':
      return 'warning';
    case 'error':
    case 'critical':
    case 'fatal':
      return 'error';
    case 'information':
    case 'info':
      return 'info';
    case 'debug':
    case 'trace':
      return 'default';
    default:
      return 'success';
  }
}

const auditActionLabels: Record<string, string> = {
  USER_LOGIN_SUCCESS: '登录',
  USER_LOGIN_FAILED: '登录失败',
  ORDER_INITIAL_SYNC: '订单同步',
  ORDER_SUBMITTED: '订单提交',
  ORDER_STARTED: '订单启动',
  ORDER_COMPLETED: '订单完成',
  ORDER_CANCELLED: '订单取消',
  ORDER_FAILED: '订单失败',
  UPDATE_CONFIG: '配置更新',
  SAVE_NODE: '保存站点',
  SAVE_PATH: '保存航道',
  SAVE_MUTEX_ZONE: '保存互斥区',
  PUBLISH_ROAD_NETWORK: '发布路网',
  FORCE_RELEASE_MUTEX: '释放互斥区',
  CONFIG_UPDATE: '配置更新',
  DISPATCH_TASK: '任务派发',
  VEHICLE_ESTOP: '车辆急停'
};

export function auditActionLabel(action?: string | null) {
  if (!action) return '事件';
  return auditActionLabels[action] || action.replaceAll('_', ' ');
}

const actorLabels: Record<string, string> = {
  Admin: '管理员',
  R_SUPER: '管理员',
  Engineer: '工程师',
  Operator: '操作员',
  Viewer: '观察员',
  DISPATCH_ENGINE: '调度引擎'
};

export function actorLabel(name?: string | null) {
  if (!name) return '';
  return actorLabels[name] || name;
}

export function humanizeStatusText(text?: string | null) {
  if (!text) return '';
  return text
    .replace(
      /\b(WaitingAction|Assigned|Executing|Running|Completed|Done|Pending|Queued|Allocated|Failed|Canceled|Cancelled|Paused|Information|Warning|Error)\b/g,
      token => {
        const level = logLevelLabels[token.toLowerCase()];
        if (level) return level;
        return orderStatusLabel(token);
      }
    )
    .replace(/\b(R_SUPER|DISPATCH_ENGINE|Admin|Engineer|Operator|Viewer)\b/g, token => actorLabels[token] || token);
}

export function nodeCaption(
  id?: string | null,
  nodes: Array<{ id: string; name?: string }> = []
) {
  if (!id || id === 'DEST') return '未指定';
  const hit = nodes.find(node => node.id === id || node.name === id);
  return hit?.name || id;
}

export function vehicleCaption(
  id?: string | null,
  vehicles: Array<{ id: string; name?: string }> = []
) {
  if (!id) return '未分配';
  const raw = String(id);
  const hit = vehicles.find(vehicle => vehicle.id === raw || vehicle.name === raw);
  if (hit?.name && hit.name !== hit.id) return hit.name;
  if (hit && /^\d+$/.test(hit.id)) return `AMR-${hit.id.padStart(2, '0')}`;
  if (/^\d+$/.test(raw)) return `AMR-${raw.padStart(2, '0')}`;
  return hit?.name || raw;
}

export const stageClass =
  'relative overflow-hidden rounded-2xl border border-slate-200 bg-slate-100 dark:border-slate-800 dark:bg-[#071018]';

export const panelClass =
  'rounded-2xl border border-slate-200/90 bg-white/95 text-slate-800 shadow-lg backdrop-blur dark:border-white/10 dark:bg-[#0c1626]/92 dark:text-slate-100';

/** 后端时间是不带时区的 UTC。补上 Z 后再按浏览器本地时区显示。 */
export function formatTimestamp(value?: string | number | null) {
  if (value === undefined || value === null || value === '') return '';
  if (typeof value === 'number') {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
  }
  const raw = value.trim();
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(raw);
  const normalized = hasZone ? raw : `${raw.replace(' ', 'T')}Z`;
  const date = new Date(normalized);
  if (Number.isNaN(date.getTime())) return raw;
  const ms = raw.match(/\.(\d+)/);
  const fraction = ms ? `.${ms[1].slice(0, 3).padEnd(3, '0')}` : '';
  return `${date.toLocaleString()}${fraction}`;
}

export function formatLogLine(line: string) {
  return line.replace(
    /^(\d{4}-\d{2}-\d{2})[ T](\d{2}:\d{2}:\d{2}(?:\.\d+)?)/,
    (_match, day: string, time: string) => formatTimestamp(`${day}T${time}`)
  );
}

const taskSerialPattern =
  /(?:task_serial|taskSerial|orderSerial|OrderSerial|任务流水号)\s*["']?\s*[:=]\s*["']?([A-Za-z0-9._-]{3,80})/i;
const vehicleKeyPattern =
  /(?:vehicleId|VehicleId|AGV_serial|agv_serial|BindVehicleId|assignedVehicleId)\s*["']?\s*[:=]\s*["']?([A-Za-z0-9._-]{1,64})/i;
const namedVehiclePattern = /\b(?:AMR|AGV)[-_]?\d{1,4}\b/i;
const nonVehicleActors = new Set([
  'SYSTEM',
  'FMS',
  'anonymous',
  'scheduler',
  'Admin',
  'SYS_DAEMON',
  'VDA_BRIDGE',
  'SAFETY_AGENT',
  'DISPATCH_ENGINE',
  'Engineer',
  'Operator',
  'Viewer',
  'R_SUPER'
]);

function logHaystack(row: Pick<Fms.LogEventEntry, 'traceId' | 'actor' | 'message' | 'path' | 'payloadJson'>) {
  return [row.payloadJson, row.message, row.path, row.actor, row.traceId].filter(Boolean).join('\n');
}

export function extractLogTaskSerial(row: Pick<Fms.LogEventEntry, 'taskSerial' | 'traceId' | 'actor' | 'message' | 'path' | 'payloadJson'>) {
  if (row.taskSerial) return row.taskSerial;
  const match = logHaystack(row).match(taskSerialPattern);
  if (match?.[1]) return match[1];
  const traceId = row.traceId || '';
  if (traceId && !traceId.includes(':') && !traceId.toUpperCase().startsWith('0H')) return traceId;
  return '';
}

export function extractLogVehicleId(row: Pick<Fms.LogEventEntry, 'vehicleId' | 'traceId' | 'actor' | 'message' | 'path' | 'payloadJson'>) {
  if (row.vehicleId) return row.vehicleId;
  const hay = logHaystack(row);
  const keyed = hay.match(vehicleKeyPattern);
  if (keyed?.[1] && !nonVehicleActors.has(keyed[1]) && keyed[1] !== '0' && keyed[1] !== 'auto') return keyed[1];
  const named = hay.match(namedVehiclePattern);
  if (named?.[0]) return named[0].toUpperCase();
  const actor = row.actor || '';
  if (!nonVehicleActors.has(actor) && /^(?:AMR|AGV)[-_]?\d{1,4}$/i.test(actor)) return actor;
  if (/^\d{1,4}$/.test(actor) && actor !== '0') return actor;
  return '';
}

export function normalizeLogEvent(row: Fms.LogEventEntry): Fms.LogEventEntry {
  return {
    ...row,
    taskSerial: extractLogTaskSerial(row) || null,
    vehicleId: extractLogVehicleId(row) || null
  };
}
