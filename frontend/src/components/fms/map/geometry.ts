import type { Bounds } from './constants';

export interface Pt {
  x: number;
  y: number;
}

export function sampleBezier(
  x0: number,
  y0: number,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  x3: number,
  y3: number,
  steps: number
): Pt[] {
  const points: Pt[] = [];
  for (let i = 0; i <= steps; i += 1) {
    const t = i / steps;
    const u = 1 - t;
    points.push({
      x: u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
      y: u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3
    });
  }
  return points;
}

/** 沿折线向两侧平移得到路沿 */
export function offsetPolyline(points: Pt[], distance: number) {
  const left: Pt[] = [];
  const right: Pt[] = [];
  for (let i = 0; i < points.length; i += 1) {
    const prev = points[Math.max(0, i - 1)];
    const next = points[Math.min(points.length - 1, i + 1)];
    let dx = next.x - prev.x;
    let dy = next.y - prev.y;
    const length = Math.hypot(dx, dy) || 1;
    dx /= length;
    dy /= length;
    left.push({ x: points[i].x - dy * distance, y: points[i].y + dx * distance });
    right.push({ x: points[i].x + dy * distance, y: points[i].y - dx * distance });
  }
  return { left, right };
}

export function polylineLength(points: Pt[]) {
  let total = 0;
  for (let i = 1; i < points.length; i += 1) total += Math.hypot(points[i].x - points[i - 1].x, points[i].y - points[i - 1].y);
  return total;
}

export interface PolylinePoint extends Pt {
  angle: number;
}

/** 按距离在折线上取点（带切线方向）。distances 需升序。 */
export function pointsAtDistances(points: Pt[], distances: number[]): PolylinePoint[] {
  const out: PolylinePoint[] = [];
  if (points.length < 2) return out;
  let seg = 0;
  let segStart = 0;
  let segLen = Math.hypot(points[1].x - points[0].x, points[1].y - points[0].y);
  for (const d of distances) {
    while (seg < points.length - 2 && d > segStart + segLen) {
      segStart += segLen;
      seg += 1;
      segLen = Math.hypot(points[seg + 1].x - points[seg].x, points[seg + 1].y - points[seg].y);
    }
    const a = points[seg];
    const b = points[seg + 1];
    const t = segLen > 1e-9 ? Math.min(1, Math.max(0, (d - segStart) / segLen)) : 0;
    out.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, angle: Math.atan2(b.y - a.y, b.x - a.x) });
  }
  return out;
}

export function distToSegmentSq(px: number, py: number, ax: number, ay: number, bx: number, by: number) {
  const abx = bx - ax;
  const aby = by - ay;
  const lenSq = abx * abx + aby * aby;
  let t = lenSq > 1e-12 ? ((px - ax) * abx + (py - ay) * aby) / lenSq : 0;
  t = Math.max(0, Math.min(1, t));
  const dx = ax + abx * t - px;
  const dy = ay + aby * t - py;
  return dx * dx + dy * dy;
}

export function pointInPolygon(x: number, y: number, polygon: Pt[]) {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i, i += 1) {
    const xi = polygon[i].x;
    const yi = polygon[i].y;
    const xj = polygon[j].x;
    const yj = polygon[j].y;
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

export function boundsOfPoints(points: Pt[]): Bounds {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const p of points) {
    if (p.x < minX) minX = p.x;
    if (p.y < minY) minY = p.y;
    if (p.x > maxX) maxX = p.x;
    if (p.y > maxY) maxY = p.y;
  }
  return { minX, minY, maxX, maxY };
}

export function boundsIntersect(a: Bounds, b: Bounds) {
  return a.minX <= b.maxX && a.maxX >= b.minX && a.minY <= b.maxY && a.maxY >= b.minY;
}

export function boundsContain(outer: Bounds, inner: Bounds) {
  return inner.minX >= outer.minX && inner.maxX <= outer.maxX && inner.minY >= outer.minY && inner.maxY <= outer.maxY;
}

export function expandBounds(b: Bounds, mx: number, my = mx): Bounds {
  return { minX: b.minX - mx, minY: b.minY - my, maxX: b.maxX + mx, maxY: b.maxY + my };
}

export function polygonCentroid(points: Pt[]): Pt {
  if (!points.length) return { x: 0, y: 0 };
  let x = 0;
  let y = 0;
  for (const p of points) {
    x += p.x;
    y += p.y;
  }
  return { x: x / points.length, y: y / points.length };
}
