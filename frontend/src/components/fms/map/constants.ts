/** 世界坐标 1 米对应的基准像素（容器缩放为 1 时） */
export const SCALE = 40;

/** 容器缩放下限 / 上限（容器缩放 × SCALE = 每米像素） */
export const MIN_ZOOM = 0.02;
export const MAX_ZOOM = 14;

/** 聚焦车辆、站点时默认的每米像素 */
export const FOCUS_PX_PER_M = 28;

/** 细节分级：按每米像素划分。数值越大，画的细节越多。 */
export type LodTier = 0 | 1 | 2 | 3;

export const TIER_NAMES = ['概览', '区域', '站点', '细节'] as const;

export function lodTier(pxPerMeter: number): LodTier {
  if (pxPerMeter < 4) return 0;
  if (pxPerMeter < 10) return 1;
  if (pxPerMeter < 16) return 2;
  return 3;
}

/** 车辆真实外形（米） */
export const VEHICLE_LENGTH_M = 0.96;
export const VEHICLE_WIDTH_M = 0.64;

export interface Bounds {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface CameraState {
  /** 视图中心，局部像素坐标（世界米 × SCALE，Y 翻转） */
  cx: number;
  cy: number;
  /** 容器缩放，每米像素 = scale × SCALE */
  scale: number;
  /** 弧度，顺时针为正 */
  rotation: number;
}

/** 对外的飞行目标：世界米坐标、每米像素、角度制旋转 */
export interface FlyTarget {
  x?: number;
  y?: number;
  /** 每米像素 */
  scale?: number;
  /** 度，顺时针为正 */
  rotation?: number;
}

export type PickResult =
  | { kind: 'vehicle'; id: string }
  | { kind: 'node'; id: string }
  | { kind: 'path'; id: string }
  | null;

export interface MapBookmark {
  id: string;
  name: string;
  /** 世界米坐标 */
  x: number;
  y: number;
  /** 每米像素 */
  scale: number;
  /** 度 */
  rotation: number;
  createdAt: number;
}

export type ViewPreset = 'global' | 'follow' | 'task' | 'alert';
