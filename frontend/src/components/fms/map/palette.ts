export interface Palette {
  dark: boolean;
  bg: number;
  gridMinor: number;
  gridMajor: number;
  gridText: number;
  /** 车道主线 */
  laneInk: number;
  /** 概览层的车道骨架 */
  laneFaint: number;
  /** 车道外描边，与底色一致，用来在交叉处隔开线条 */
  laneCasing: number;
  laneEdge: number;
  text: number;
  textMuted: number;
  /** 文字描边、图标外圈，与底色一致 */
  halo: number;
  accent: number;
  select: number;
  hover: number;
  zoneFree: number;
  zoneBusy: number;
  danger: number;
  cloud: [number, number, number, number];
}

const light: Palette = {
  dark: false,
  bg: 0xf7f8fa,
  gridMinor: 0xeceff3,
  gridMajor: 0xdfe4ea,
  gridText: 0x8793a0,
  laneInk: 0x2f3d4c,
  laneFaint: 0x66768a,
  laneCasing: 0xf7f8fa,
  laneEdge: 0xaab5c1,
  text: 0x1b2733,
  textMuted: 0x5e6b78,
  halo: 0xf7f8fa,
  accent: 0x1d5fd1,
  select: 0xc2570c,
  hover: 0x0b6bcb,
  zoneFree: 0xd99a1e,
  zoneBusy: 0xd23a52,
  danger: 0xc62828,
  cloud: [71, 85, 105, 150]
};

const dark: Palette = {
  dark: true,
  bg: 0x101418,
  gridMinor: 0x171d24,
  gridMajor: 0x212b36,
  gridText: 0x66768a,
  laneInk: 0xc9d4df,
  laneFaint: 0x8191a3,
  laneCasing: 0x101418,
  laneEdge: 0x445364,
  text: 0xe6edf4,
  textMuted: 0x93a1b0,
  halo: 0x101418,
  accent: 0x5aa7ff,
  select: 0xf5a524,
  hover: 0x7cc0ff,
  zoneFree: 0xe0a030,
  zoneBusy: 0xef5b73,
  danger: 0xf0605d,
  cloud: [148, 163, 184, 140]
};

export function getPalette(isDark: boolean): Palette {
  return isDark ? dark : light;
}

const vehicleLight: Record<string, number> = {
  Running: 0x2b6cd9,
  Assigned: 0x2b6cd9,
  Idle: 0x13897f,
  WaitingLock: 0xc77d12,
  Avoiding: 0xc12a52,
  Error: 0xc62828,
  Fault: 0xc62828,
  Estop: 0xc62828,
  Charging: 0x7c4ddb,
  Paused: 0x5f6f82,
  Offline: 0x9aa5b1
};

const vehicleDark: Record<string, number> = {
  Running: 0x4d90f5,
  Assigned: 0x4d90f5,
  Idle: 0x2fbfb0,
  WaitingLock: 0xe9a23a,
  Avoiding: 0xee6a8c,
  Error: 0xf0605d,
  Fault: 0xf0605d,
  Estop: 0xf0605d,
  Charging: 0xa78bfa,
  Paused: 0x93a1b0,
  Offline: 0x66727f
};

export function vehicleColor(status: string | undefined, isDark: boolean) {
  const table = isDark ? vehicleDark : vehicleLight;
  return table[status || ''] ?? table.Running;
}

const stationLight = {
  charge: 0xb45309,
  job: 0x1d4e89,
  standby: 0x0f766e,
  pullover: 0x6d4fc2,
  landmark: 0x748394
};

const stationDark = {
  charge: 0xf0a53a,
  job: 0x4f9bf0,
  standby: 0x39c2b2,
  pullover: 0xa98bff,
  landmark: 0x8696a8
};

export type StationKind = keyof typeof stationLight;

export function stationColor(kind: StationKind, isDark: boolean) {
  return (isDark ? stationDark : stationLight)[kind];
}

export function batteryColor(percent: number, isDark: boolean) {
  if (percent <= 20) return isDark ? 0xf0605d : 0xd32f2f;
  if (percent <= 45) return isDark ? 0xe9a23a : 0xd98a0b;
  return isDark ? 0x3ccf8e : 0x1f9d63;
}

/** 按 0-1 系数把颜色调暗 */
export function shade(color: number, factor: number) {
  const r = Math.round(((color >> 16) & 0xff) * factor);
  const g = Math.round(((color >> 8) & 0xff) * factor);
  const b = Math.round((color & 0xff) * factor);
  return (r << 16) | (g << 8) | b;
}

/** 与目标色混合，amount 为 0-1 */
export function mix(color: number, target: number, amount: number) {
  const r1 = (color >> 16) & 0xff;
  const g1 = (color >> 8) & 0xff;
  const b1 = color & 0xff;
  const r2 = (target >> 16) & 0xff;
  const g2 = (target >> 8) & 0xff;
  const b2 = target & 0xff;
  return (Math.round(r1 + (r2 - r1) * amount) << 16) | (Math.round(g1 + (g2 - g1) * amount) << 8) | Math.round(b1 + (b2 - b1) * amount);
}

export function cssColor(color: number) {
  return `#${color.toString(16).padStart(6, '0')}`;
}
