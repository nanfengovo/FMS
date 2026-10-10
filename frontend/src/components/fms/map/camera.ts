import { MAX_ZOOM, MIN_ZOOM } from './constants';
import type { Bounds, CameraState } from './constants';

const RHO = Math.SQRT2;
const RHO2 = RHO * RHO;
const RHO4 = RHO2 * RHO2;
const TWO_PI = Math.PI * 2;

export function clamp(value: number, min: number, max: number) {
  return Math.max(min, Math.min(max, value));
}

export function easeInOutCubic(t: number) {
  return t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2;
}

/** 返回 from 转到 to 的最短有符号角度差 */
export function shortestAngle(from: number, to: number) {
  let diff = (to - from) % TWO_PI;
  if (diff > Math.PI) diff -= TWO_PI;
  if (diff < -Math.PI) diff += TWO_PI;
  return diff;
}

export function normalizeAngle(angle: number) {
  return shortestAngle(0, angle);
}

const cosh = (x: number) => (Math.exp(x) + Math.exp(-x)) / 2;
const sinh = (x: number) => (Math.exp(x) - Math.exp(-x)) / 2;
const tanh = (x: number) => {
  if (x > 20) return 1;
  if (x < -20) return -1;
  const a = Math.exp(2 * x);
  return (a - 1) / (a + 1);
};

interface Flight {
  target: CameraState;
  from: CameraState;
  duration: number;
  elapsed: number;
  interp: (t: number) => [number, number, number];
  rotationDelta: number;
}

export interface FlyOptions {
  /** 固定时长（毫秒）。省略时按距离与缩放跨度自动估算 */
  duration?: number;
  /** 速度倍率，默认 1 */
  speed?: number;
}

export interface FitOptions {
  padding?: number;
  minScale?: number;
  maxScale?: number;
  rotation?: number;
  insets?: { left?: number; top?: number; right?: number; bottom?: number };
}

/**
 * 地图相机：以 (cx, cy) 为视图中心，带缩放与旋转。
 * 屏幕坐标 = 视口中心 + R(rotation) · scale · (局部坐标 − (cx, cy))。
 * 飞行动画使用 van Wijk 平滑缩放平移曲线：长距离跳转时先拉远再推近。
 */
export class Camera {
  cx = 0;
  cy = 0;
  scale = 1;
  rotation = 0;
  width = 800;
  height = 600;

  private flight: Flight | null = null;

  get flying() {
    return this.flight !== null;
  }

  state(): CameraState {
    return { cx: this.cx, cy: this.cy, scale: this.scale, rotation: this.rotation };
  }

  set(next: Partial<CameraState>) {
    if (next.cx !== undefined) this.cx = next.cx;
    if (next.cy !== undefined) this.cy = next.cy;
    if (next.scale !== undefined) this.scale = clamp(next.scale, MIN_ZOOM, MAX_ZOOM);
    if (next.rotation !== undefined) this.rotation = next.rotation;
  }

  setViewport(width: number, height: number) {
    this.width = Math.max(1, width);
    this.height = Math.max(1, height);
  }

  screenToLocal(sx: number, sy: number) {
    const dx = sx - this.width / 2;
    const dy = sy - this.height / 2;
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    return {
      x: this.cx + (c * dx + s * dy) / this.scale,
      y: this.cy + (-s * dx + c * dy) / this.scale
    };
  }

  localToScreen(lx: number, ly: number) {
    const dx = (lx - this.cx) * this.scale;
    const dy = (ly - this.cy) * this.scale;
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    return {
      x: this.width / 2 + c * dx - s * dy,
      y: this.height / 2 + s * dx + c * dy
    };
  }

  panBy(screenDx: number, screenDy: number) {
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    this.cx -= (c * screenDx + s * screenDy) / this.scale;
    this.cy -= (-s * screenDx + c * screenDy) / this.scale;
  }

  /** 以屏幕点为锚缩放，锚点下的地图位置保持不动 */
  zoomAt(sx: number, sy: number, nextScale: number) {
    const before = this.screenToLocal(sx, sy);
    this.scale = clamp(nextScale, MIN_ZOOM, MAX_ZOOM);
    const after = this.screenToLocal(sx, sy);
    this.cx += before.x - after.x;
    this.cy += before.y - after.y;
  }

  /** 以屏幕点为锚旋转 */
  rotateAt(sx: number, sy: number, nextRotation: number) {
    const before = this.screenToLocal(sx, sy);
    this.rotation = nextRotation;
    const after = this.screenToLocal(sx, sy);
    this.cx += before.x - after.x;
    this.cy += before.y - after.y;
  }

  /** 视口四角对应的局部坐标，顺序：左上、右上、右下、左下 */
  viewQuad() {
    return [
      this.screenToLocal(0, 0),
      this.screenToLocal(this.width, 0),
      this.screenToLocal(this.width, this.height),
      this.screenToLocal(0, this.height)
    ];
  }

  viewBounds(margin = 0): Bounds {
    const quad = this.viewQuad();
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    for (const p of quad) {
      if (p.x < minX) minX = p.x;
      if (p.y < minY) minY = p.y;
      if (p.x > maxX) maxX = p.x;
      if (p.y > maxY) maxY = p.y;
    }
    const mx = (maxX - minX) * margin;
    const my = (maxY - minY) * margin;
    return { minX: minX - mx, minY: minY - my, maxX: maxX + mx, maxY: maxY + my };
  }

  /** 计算刚好容纳一组点（flat [x0,y0,x1,y1,...]，局部像素）的相机状态，不直接应用 */
  fitPoints(flat: ArrayLike<number>, options: FitOptions = {}): CameraState | null {
    if (flat.length < 2) return null;
    const rotation = options.rotation ?? this.rotation;
    const c = Math.cos(rotation);
    const s = Math.sin(rotation);
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    for (let i = 0; i + 1 < flat.length; i += 2) {
      const x = flat[i];
      const y = flat[i + 1];
      // 旋转到与屏幕对齐的坐标系（相当于只看旋转、不看平移的屏幕方向）
      const qx = c * x - s * y;
      const qy = s * x + c * y;
      if (qx < minX) minX = qx;
      if (qy < minY) minY = qy;
      if (qx > maxX) maxX = qx;
      if (qy > maxY) maxY = qy;
    }
    const padding = options.padding ?? 56;
    const insets = options.insets ?? {};
    const left = insets.left ?? 0;
    const right = insets.right ?? 0;
    const top = insets.top ?? 0;
    const bottom = insets.bottom ?? 0;
    const availW = Math.max(60, this.width - padding * 2 - left - right);
    const availH = Math.max(60, this.height - padding * 2 - top - bottom);
    const boxW = Math.max(maxX - minX, 1e-6);
    const boxH = Math.max(maxY - minY, 1e-6);
    const scale = clamp(Math.min(availW / boxW, availH / boxH), options.minScale ?? MIN_ZOOM, options.maxScale ?? MAX_ZOOM);
    const qcx = (minX + maxX) / 2;
    const qcy = (minY + maxY) / 2;
    const centerX = c * qcx + s * qcy;
    const centerY = -s * qcx + c * qcy;
    const offsetX = (left - right) / 2;
    const offsetY = (top - bottom) / 2;
    return {
      cx: centerX - (c * offsetX + s * offsetY) / scale,
      cy: centerY - (-s * offsetX + c * offsetY) / scale,
      scale,
      rotation
    };
  }

  cancelFlight() {
    this.flight = null;
  }

  /** 开始带缓动的飞行。返回 false 表示目标与当前几乎重合，已直接应用 */
  flyTo(target: CameraState, options: FlyOptions = {}) {
    const from = this.state();
    const goal: CameraState = {
      cx: target.cx,
      cy: target.cy,
      scale: clamp(target.scale, MIN_ZOOM, MAX_ZOOM),
      rotation: from.rotation + shortestAngle(from.rotation, target.rotation)
    };
    const w0 = this.width / from.scale;
    const w1 = this.width / goal.scale;
    const dx = goal.cx - from.cx;
    const dy = goal.cy - from.cy;
    const d2 = dx * dx + dy * dy;
    const rotationDelta = goal.rotation - from.rotation;

    if (d2 < 0.04 && Math.abs(Math.log(w1 / w0)) < 0.002 && Math.abs(rotationDelta) < 0.002) {
      this.set(goal);
      this.flight = null;
      return false;
    }

    let span: number;
    let interp: (t: number) => [number, number, number];
    if (d2 < 1e-6) {
      span = Math.log(w1 / w0) / RHO;
      interp = t => [from.cx, from.cy, w0 * Math.exp(RHO * t * span)];
    } else {
      const d1 = Math.sqrt(d2);
      const b0 = (w1 * w1 - w0 * w0 + RHO4 * d2) / (2 * w0 * RHO2 * d1);
      const b1 = (w1 * w1 - w0 * w0 - RHO4 * d2) / (2 * w1 * RHO2 * d1);
      const r0 = Math.log(Math.sqrt(b0 * b0 + 1) - b0);
      const r1 = Math.log(Math.sqrt(b1 * b1 + 1) - b1);
      span = (r1 - r0) / RHO;
      const coshR0 = cosh(r0);
      const sinhR0 = sinh(r0);
      interp = t => {
        const s = t * span;
        const u = (w0 / (RHO2 * d1)) * (coshR0 * tanh(RHO * s + r0) - sinhR0);
        return [from.cx + u * dx, from.cy + u * dy, (w0 * coshR0) / cosh(RHO * s + r0)];
      };
    }

    const auto = Math.abs(span) * 1000;
    const duration = options.duration ?? clamp(auto / (options.speed ?? 1), 340, 1400);
    this.flight = { target: goal, from, duration, elapsed: 0, interp, rotationDelta };
    return true;
  }

  /** 推进飞行动画；有变化返回 true */
  step(dtMs: number) {
    const flight = this.flight;
    if (!flight) return false;
    flight.elapsed = Math.min(flight.duration, flight.elapsed + dtMs);
    const t = flight.elapsed / flight.duration;
    if (t >= 1) {
      this.set(flight.target);
      this.rotation = flight.target.rotation;
      this.flight = null;
      return true;
    }
    const e = easeInOutCubic(t);
    const [x, y, w] = flight.interp(e);
    this.cx = x;
    this.cy = y;
    this.scale = clamp(this.width / w, MIN_ZOOM, MAX_ZOOM);
    this.rotation = flight.from.rotation + flight.rotationDelta * e;
    return true;
  }
}
