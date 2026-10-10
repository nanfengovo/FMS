import { Camera } from '../src/components/fms/map/camera';
import { searchItems } from '../src/components/fms/map/search';
import { pointsAtDistances, offsetPolyline } from '../src/components/fms/map/geometry';

function assert(cond: boolean, msg: string) {
  if (!cond) throw new Error(`FAIL: ${msg}`);
  console.log(`ok   ${msg}`);
}
const close = (a: number, b: number, eps = 1e-6) => Math.abs(a - b) < eps;

const cam = new Camera();
cam.setViewport(800, 600);

for (const rot of [0, 0.6, -1.9, Math.PI]) {
  cam.set({ cx: 123, cy: -456, scale: 0.7, rotation: rot });
  const p = cam.localToScreen(200, -300);
  const q = cam.screenToLocal(p.x, p.y);
  assert(close(q.x, 200) && close(q.y, -300), `round trip rot=${rot.toFixed(2)}`);
}

cam.set({ cx: 50, cy: 80, scale: 1, rotation: 0.8 });
const anchor = { x: 210, y: 130 };
const before = cam.screenToLocal(anchor.x, anchor.y);
cam.zoomAt(anchor.x, anchor.y, 2.5);
const after = cam.screenToLocal(anchor.x, anchor.y);
assert(close(before.x, after.x, 1e-6) && close(before.y, after.y, 1e-6), 'zoomAt keeps anchor');

cam.set({ cx: 0, cy: 0, scale: 1, rotation: 0.4 });
const b0 = cam.screenToLocal(300, 200);
cam.panBy(40, -25);
const b1 = cam.localToScreen(b0.x, b0.y);
assert(close(b1.x, 340, 1e-6) && close(b1.y, 175, 1e-6), 'panBy moves content with pointer');

const pts = [-500, -300, 900, 100, 200, 800, -100, -700];
for (const rot of [0, 0.5, 2.2]) {
  const target = cam.fitPoints(pts, { padding: 40, rotation: rot });
  assert(!!target && Number.isFinite(target!.scale), `fitPoints finite rot=${rot}`);
  cam.set(target!);
  let inside = true;
  for (let i = 0; i < pts.length; i += 2) {
    const s = cam.localToScreen(pts[i], pts[i + 1]);
    if (s.x < 39 || s.x > 761 || s.y < 39 || s.y > 561) inside = false;
  }
  assert(inside, `fitPoints keeps points inside padding rot=${rot}`);
}

// flight
cam.set({ cx: 0, cy: 0, scale: 0.2, rotation: 0 });
const goal = { cx: 4000, cy: -2500, scale: 1.4, rotation: 1.2 };
const started = cam.flyTo(goal);
assert(started && cam.flying, 'flight starts');
let frames = 0;
let maxLogScale = 0;
let minScale = 1e9;
let total = 0;
while (cam.flying && frames < 5000) {
  cam.step(16);
  total += 16;
  frames += 1;
  assert(Number.isFinite(cam.cx) && Number.isFinite(cam.cy) && Number.isFinite(cam.scale) && Number.isFinite(cam.rotation), `frame ${frames} finite`) ;
  minScale = Math.min(minScale, cam.scale);
  maxLogScale = Math.max(maxLogScale, cam.scale);
  if (frames > 3) break;
}
while (cam.flying && frames < 5000) {
  cam.step(16);
  total += 16;
  frames += 1;
  minScale = Math.min(minScale, cam.scale);
}
console.log(`flight took ${total}ms, min scale along path ${minScale.toFixed(3)}`);
assert(close(cam.cx, goal.cx, 1e-6) && close(cam.cy, goal.cy, 1e-6) && close(cam.scale, goal.scale, 1e-9) && close(cam.rotation, goal.rotation, 1e-9), 'flight ends exactly at the goal');
assert(minScale < 0.2, 'long flight zooms out in the middle');
assert(total >= 340 && total <= 1500, 'flight duration is within bounds');

const noop = cam.flyTo(cam.state());
assert(noop === false && !cam.flying, 'flyTo same state is a no-op');

// search
const items = [
  { kind: 'vehicle' as const, id: 'BENCH-AMR-25', title: 'AMR-25', subtitle: '', keys: ['bench-amr-25', 'amr-25'] },
  { kind: 'vehicle' as const, id: 'BENCH-AMR-02', title: 'AMR-02', subtitle: '', keys: ['bench-amr-02'] },
  { kind: 'node' as const, id: '25', title: '25', subtitle: '', keys: ['25', ''] },
  { kind: 'node' as const, id: '1250', title: '1250', subtitle: '', keys: ['1250', ''] },
  { kind: 'order' as const, id: 'o1', title: 'INT-874607', subtitle: '', keys: ['int-874607', 'o1'] }
];
const hits = searchItems(items, '25');
assert(hits[0].id === 'BENCH-AMR-25', 'digit query prefers vehicle suffix match first');
assert(hits.some(h => h.kind === 'node' && h.id === '25'), 'digit query finds exact node');
const nodeHits = hits.filter(h => h.kind === 'node');
assert(nodeHits[0].id === '25', 'exact node id ranks before prefix node id');
assert(searchItems(items, 'int-8').length === 1, 'order serial prefix works');

// geometry
const line = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }];
const marks = pointsAtDistances(line, [50, 100, 150]);
assert(close(marks[0].x, 50) && close(marks[0].angle, 0), 'pointsAtDistances first segment');
assert(close(marks[2].y, 50) && close(marks[2].angle, Math.PI / 2), 'pointsAtDistances second segment');
const edges = offsetPolyline([{ x: 0, y: 0 }, { x: 10, y: 0 }], 2);
assert(close(edges.left[0].y, 2) && close(edges.right[0].y, -2), 'offsetPolyline sides');
console.log('ALL OK');
