// src/components/fms/map/constants.ts
var MIN_ZOOM = 0.02;
var MAX_ZOOM = 14;

// src/components/fms/map/camera.ts
var RHO = Math.SQRT2;
var RHO2 = RHO * RHO;
var RHO4 = RHO2 * RHO2;
var TWO_PI = Math.PI * 2;
function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}
function easeInOutCubic(t) {
  return t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2;
}
function shortestAngle(from, to) {
  let diff = (to - from) % TWO_PI;
  if (diff > Math.PI) diff -= TWO_PI;
  if (diff < -Math.PI) diff += TWO_PI;
  return diff;
}
var cosh = (x) => (Math.exp(x) + Math.exp(-x)) / 2;
var sinh = (x) => (Math.exp(x) - Math.exp(-x)) / 2;
var tanh = (x) => {
  if (x > 20) return 1;
  if (x < -20) return -1;
  const a = Math.exp(2 * x);
  return (a - 1) / (a + 1);
};
var Camera = class {
  cx = 0;
  cy = 0;
  scale = 1;
  rotation = 0;
  width = 800;
  height = 600;
  flight = null;
  get flying() {
    return this.flight !== null;
  }
  state() {
    return { cx: this.cx, cy: this.cy, scale: this.scale, rotation: this.rotation };
  }
  set(next) {
    if (next.cx !== void 0) this.cx = next.cx;
    if (next.cy !== void 0) this.cy = next.cy;
    if (next.scale !== void 0) this.scale = clamp(next.scale, MIN_ZOOM, MAX_ZOOM);
    if (next.rotation !== void 0) this.rotation = next.rotation;
  }
  setViewport(width, height) {
    this.width = Math.max(1, width);
    this.height = Math.max(1, height);
  }
  screenToLocal(sx, sy) {
    const dx = sx - this.width / 2;
    const dy = sy - this.height / 2;
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    return {
      x: this.cx + (c * dx + s * dy) / this.scale,
      y: this.cy + (-s * dx + c * dy) / this.scale
    };
  }
  localToScreen(lx, ly) {
    const dx = (lx - this.cx) * this.scale;
    const dy = (ly - this.cy) * this.scale;
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    return {
      x: this.width / 2 + c * dx - s * dy,
      y: this.height / 2 + s * dx + c * dy
    };
  }
  panBy(screenDx, screenDy) {
    const c = Math.cos(this.rotation);
    const s = Math.sin(this.rotation);
    this.cx -= (c * screenDx + s * screenDy) / this.scale;
    this.cy -= (-s * screenDx + c * screenDy) / this.scale;
  }
  /** 以屏幕点为锚缩放，锚点下的地图位置保持不动 */
  zoomAt(sx, sy, nextScale) {
    const before2 = this.screenToLocal(sx, sy);
    this.scale = clamp(nextScale, MIN_ZOOM, MAX_ZOOM);
    const after2 = this.screenToLocal(sx, sy);
    this.cx += before2.x - after2.x;
    this.cy += before2.y - after2.y;
  }
  /** 以屏幕点为锚旋转 */
  rotateAt(sx, sy, nextRotation) {
    const before2 = this.screenToLocal(sx, sy);
    this.rotation = nextRotation;
    const after2 = this.screenToLocal(sx, sy);
    this.cx += before2.x - after2.x;
    this.cy += before2.y - after2.y;
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
  viewBounds(margin = 0) {
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
  fitPoints(flat, options = {}) {
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
  flyTo(target, options = {}) {
    const from = this.state();
    const goal2 = {
      cx: target.cx,
      cy: target.cy,
      scale: clamp(target.scale, MIN_ZOOM, MAX_ZOOM),
      rotation: from.rotation + shortestAngle(from.rotation, target.rotation)
    };
    const w0 = this.width / from.scale;
    const w1 = this.width / goal2.scale;
    const dx = goal2.cx - from.cx;
    const dy = goal2.cy - from.cy;
    const d2 = dx * dx + dy * dy;
    const rotationDelta = goal2.rotation - from.rotation;
    if (d2 < 0.04 && Math.abs(Math.log(w1 / w0)) < 2e-3 && Math.abs(rotationDelta) < 2e-3) {
      this.set(goal2);
      this.flight = null;
      return false;
    }
    let span;
    let interp;
    if (d2 < 1e-6) {
      span = Math.log(w1 / w0) / RHO;
      interp = (t) => [from.cx, from.cy, w0 * Math.exp(RHO * t * span)];
    } else {
      const d1 = Math.sqrt(d2);
      const b02 = (w1 * w1 - w0 * w0 + RHO4 * d2) / (2 * w0 * RHO2 * d1);
      const b12 = (w1 * w1 - w0 * w0 - RHO4 * d2) / (2 * w1 * RHO2 * d1);
      const r0 = Math.log(Math.sqrt(b02 * b02 + 1) - b02);
      const r1 = Math.log(Math.sqrt(b12 * b12 + 1) - b12);
      span = (r1 - r0) / RHO;
      const coshR0 = cosh(r0);
      const sinhR0 = sinh(r0);
      interp = (t) => {
        const s = t * span;
        const u = w0 / (RHO2 * d1) * (coshR0 * tanh(RHO * s + r0) - sinhR0);
        return [from.cx + u * dx, from.cy + u * dy, w0 * coshR0 / cosh(RHO * s + r0)];
      };
    }
    const auto = Math.abs(span) * 1e3;
    const duration = options.duration ?? clamp(auto / (options.speed ?? 1), 340, 1400);
    this.flight = { target: goal2, from, duration, elapsed: 0, interp, rotationDelta };
    return true;
  }
  /** 推进飞行动画；有变化返回 true */
  step(dtMs) {
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
};

// src/components/fms/map/search.ts
function scoreKey(key, query, digitsOnly) {
  if (!key) return 0;
  if (key === query) return 100;
  if (key.startsWith(query)) return 80;
  if (digitsOnly) {
    if (key.endsWith(`-${query}`) || key.endsWith(`_${query}`)) return 90;
    const parts = key.split(/[^0-9]+/).filter(Boolean);
    if (parts.length && parts[parts.length - 1] === query) return 76;
  }
  const at = key.indexOf(query);
  if (at > 0) return 50 - Math.min(at, 20) * 0.5;
  return 0;
}
function searchItems(items2, rawQuery, limitPerKind = 5) {
  const query = rawQuery.trim().toLowerCase();
  if (!query) return [];
  const digitsOnly = /^\d+$/.test(query);
  const hits2 = [];
  for (const item of items2) {
    let best = 0;
    for (const key of item.keys) {
      const score = scoreKey(key, query, digitsOnly);
      if (score > best) best = score;
    }
    if (best > 0) hits2.push({ ...item, score: best });
  }
  const order = ["vehicle", "order", "node"];
  const out = [];
  for (const kind of order) {
    const group = hits2.filter((hit) => hit.kind === kind).sort((a, b) => b.score - a.score || a.title.localeCompare(b.title, "zh-CN", { numeric: true })).slice(0, limitPerKind);
    out.push(...group);
  }
  return out;
}

// src/components/fms/map/geometry.ts
function offsetPolyline(points, distance) {
  const left = [];
  const right = [];
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
function pointsAtDistances(points, distances) {
  const out = [];
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

// .verify-map/camera.test.ts
function assert(cond, msg) {
  if (!cond) throw new Error(`FAIL: ${msg}`);
  console.log(`ok   ${msg}`);
}
var close = (a, b, eps = 1e-6) => Math.abs(a - b) < eps;
var cam = new Camera();
cam.setViewport(800, 600);
for (const rot of [0, 0.6, -1.9, Math.PI]) {
  cam.set({ cx: 123, cy: -456, scale: 0.7, rotation: rot });
  const p = cam.localToScreen(200, -300);
  const q = cam.screenToLocal(p.x, p.y);
  assert(close(q.x, 200) && close(q.y, -300), `round trip rot=${rot.toFixed(2)}`);
}
cam.set({ cx: 50, cy: 80, scale: 1, rotation: 0.8 });
var anchor = { x: 210, y: 130 };
var before = cam.screenToLocal(anchor.x, anchor.y);
cam.zoomAt(anchor.x, anchor.y, 2.5);
var after = cam.screenToLocal(anchor.x, anchor.y);
assert(close(before.x, after.x, 1e-6) && close(before.y, after.y, 1e-6), "zoomAt keeps anchor");
cam.set({ cx: 0, cy: 0, scale: 1, rotation: 0.4 });
var b0 = cam.screenToLocal(300, 200);
cam.panBy(40, -25);
var b1 = cam.localToScreen(b0.x, b0.y);
assert(close(b1.x, 340, 1e-6) && close(b1.y, 175, 1e-6), "panBy moves content with pointer");
var pts = [-500, -300, 900, 100, 200, 800, -100, -700];
for (const rot of [0, 0.5, 2.2]) {
  const target = cam.fitPoints(pts, { padding: 40, rotation: rot });
  assert(!!target && Number.isFinite(target.scale), `fitPoints finite rot=${rot}`);
  cam.set(target);
  let inside = true;
  for (let i = 0; i < pts.length; i += 2) {
    const s = cam.localToScreen(pts[i], pts[i + 1]);
    if (s.x < 39 || s.x > 761 || s.y < 39 || s.y > 561) inside = false;
  }
  assert(inside, `fitPoints keeps points inside padding rot=${rot}`);
}
cam.set({ cx: 0, cy: 0, scale: 0.2, rotation: 0 });
var goal = { cx: 4e3, cy: -2500, scale: 1.4, rotation: 1.2 };
var started = cam.flyTo(goal);
assert(started && cam.flying, "flight starts");
var frames = 0;
var maxLogScale = 0;
var minScale = 1e9;
var total = 0;
while (cam.flying && frames < 5e3) {
  cam.step(16);
  total += 16;
  frames += 1;
  assert(Number.isFinite(cam.cx) && Number.isFinite(cam.cy) && Number.isFinite(cam.scale) && Number.isFinite(cam.rotation), `frame ${frames} finite`);
  minScale = Math.min(minScale, cam.scale);
  maxLogScale = Math.max(maxLogScale, cam.scale);
  if (frames > 3) break;
}
while (cam.flying && frames < 5e3) {
  cam.step(16);
  total += 16;
  frames += 1;
  minScale = Math.min(minScale, cam.scale);
}
console.log(`flight took ${total}ms, min scale along path ${minScale.toFixed(3)}`);
assert(close(cam.cx, goal.cx, 1e-6) && close(cam.cy, goal.cy, 1e-6) && close(cam.scale, goal.scale, 1e-9) && close(cam.rotation, goal.rotation, 1e-9), "flight ends exactly at the goal");
assert(minScale < 0.2, "long flight zooms out in the middle");
assert(total >= 340 && total <= 1500, "flight duration is within bounds");
var noop = cam.flyTo(cam.state());
assert(noop === false && !cam.flying, "flyTo same state is a no-op");
var items = [
  { kind: "vehicle", id: "BENCH-AMR-25", title: "AMR-25", subtitle: "", keys: ["bench-amr-25", "amr-25"] },
  { kind: "vehicle", id: "BENCH-AMR-02", title: "AMR-02", subtitle: "", keys: ["bench-amr-02"] },
  { kind: "node", id: "25", title: "25", subtitle: "", keys: ["25", ""] },
  { kind: "node", id: "1250", title: "1250", subtitle: "", keys: ["1250", ""] },
  { kind: "order", id: "o1", title: "INT-874607", subtitle: "", keys: ["int-874607", "o1"] }
];
var hits = searchItems(items, "25");
assert(hits[0].id === "BENCH-AMR-25", "digit query prefers vehicle suffix match first");
assert(hits.some((h) => h.kind === "node" && h.id === "25"), "digit query finds exact node");
var nodeHits = hits.filter((h) => h.kind === "node");
assert(nodeHits[0].id === "25", "exact node id ranks before prefix node id");
assert(searchItems(items, "int-8").length === 1, "order serial prefix works");
var line = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }];
var marks = pointsAtDistances(line, [50, 100, 150]);
assert(close(marks[0].x, 50) && close(marks[0].angle, 0), "pointsAtDistances first segment");
assert(close(marks[2].y, 50) && close(marks[2].angle, Math.PI / 2), "pointsAtDistances second segment");
var edges = offsetPolyline([{ x: 0, y: 0 }, { x: 10, y: 0 }], 2);
assert(close(edges.left[0].y, 2) && close(edges.right[0].y, -2), "offsetPolyline sides");
console.log("ALL OK");
