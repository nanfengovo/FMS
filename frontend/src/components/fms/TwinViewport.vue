<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { useThemeStore } from '@/store/modules/theme';

const props = defineProps<{
  graph?: Fms.MapGraphDto | null;
  vehicles?: Fms.VehicleDto[];
  orders?: Fms.OrderDto[];
  mutexZones?: Fms.MutexZoneDto[];
  pointCloud?: Fms.Point2D[];
  raster?: { url: string; originX: number; topY: number; widthMeters: number; heightMeters: number } | null;
  selectedVehicleId?: string | null;
  selectedNodeId?: string | null;
  selectedPathId?: string | null;
}>();

const emit = defineEmits<{
  'select-vehicle': [id: string | null];
  'select-node': [id: string | null];
}>();

type Kind = 'charge' | 'job' | 'standby' | 'pullover' | 'landmark';
type WheelLayout = 'diff' | 'steer' | 'dual' | 'mecanum';
type XZ = { x: number; z: number };
type Bounds = { minX: number; maxX: number; minY: number; maxY: number };
type NodeRec = { id: string; caption: string; x: number; z: number; kind: Kind; priority: number };
type Spec = { length: number; width: number; layout: WheelLayout };

const themeStore = useThemeStore();
const host = ref<HTMLDivElement | null>(null);

let renderer: any = null;
let scene: any = null;
let camera: any = null;
let orbit: OrbitControls | null = null;
let frame = 0;
let resizeObserver: ResizeObserver | null = null;
let floor: any = null;
let floorGrid: any = null;
let staticRoot: any = null;
let overlayRoot: any = null;
let labelRoot: any = null;
let nodeRing: any = null;
let cloudMaterial: any = null;
let cloudGeometry: any = null;
let ambient: any = null;
let hemi: any = null;
let sun: any = null;
let fill: any = null;

const vehicles = new Map<string, any>();
const nodeRecs: NodeRec[] = [];
const nodeById = new Map<string, NodeRec>();
const nodeMeshes: any[] = [];
const geometries = new Map<string, any>();
const sharedGeometrySet = new Set<any>();
const sharedMaterialSet = new Set<any>();
const shared: Record<string, any> = {};
const labelTextures = new Map<string, { tex: any; aspect: number }>();
const labelSlots: Array<{ sprite: any; key: string }> = [];
const liveLabelKeys = new Set<string>();
const candRec: NodeRec[] = [];
const candDist: number[] = [];
const candSel: number[] = [];
const candOrder: number[] = [];
const placedX: number[] = [];
const placedY: number[] = [];

const pointer = new THREE.Vector2();
const raycaster = new THREE.Raycaster();
const world = new THREE.Vector3();
const ndc = new THREE.Vector3();
const frustum = new THREE.Frustum();
const proj = new THREE.Matrix4();
const dummy = new THREE.Object3D();

let mapSpan = 48;
let framedKey = '';
let pressX = 0;
let pressY = 0;
let pressButton = -1;

const MAX_LABELS = 64;

function palette(dark: boolean) {
  if (dark) {
    return {
      bg: 0x0e1318,
      floor: 0x12181f,
      gridMajor: 0x2a3c4c,
      gridMinor: 0x1a242e,
      ambient: 0x9eb0c2,
      ambientIntensity: 0.42,
      hemiSky: 0x8ea0b4,
      hemiGround: 0x10161c,
      hemiIntensity: 0.5,
      sun: 0xe6eef6,
      sunIntensity: 0.62,
      fill: 0x8aa0b8,
      fillIntensity: 0.18,
      roadFwd: 0x9eb6d0,
      roadBack: 0xd7a0ab,
      arrowFwd: 0xe7f0f7,
      arrowBack: 0xf3d0d6,
      task: 0xf5a524,
      selected: 0x7cc0ff,
      zoneFree: 0xe0a030,
      zoneBusy: 0xef5b73,
      body: 0x9aa6b4,
      deck: 0xc5ced8,
      heading: 0xe7eef5,
      wheel: 0x1c2228,
      hub: 0x6d7884,
      pallet: 0x8d7352,
      crate: 0x6e5840,
      ring: 0xf5a524,
      cloudOpacity: 0.78
    };
  }
  return {
    bg: 0xf3f5f8,
    floor: 0xe6edf3,
    gridMajor: 0xc5d0db,
    gridMinor: 0xdbe3ec,
    ambient: 0xf7f8fa,
    ambientIntensity: 0.74,
    hemiSky: 0xffffff,
    hemiGround: 0xd5dee8,
    hemiIntensity: 0.36,
    sun: 0xffffff,
    sunIntensity: 0.7,
    fill: 0xe7eef6,
    fillIntensity: 0.2,
    roadFwd: 0x3c4d60,
    roadBack: 0x8d4b5c,
    arrowFwd: 0x243444,
    arrowBack: 0x743848,
    task: 0xc2570c,
    selected: 0x1d5fd1,
    zoneFree: 0xd99a1e,
    zoneBusy: 0xd23a52,
    body: 0xd8dee6,
    deck: 0xf4f7fa,
    heading: 0x314155,
    wheel: 0x2c333c,
    hub: 0x8b95a1,
    pallet: 0xd7c4a8,
    crate: 0xc4a882,
    ring: 0xc2570c,
    cloudOpacity: 0.62
  };
}

function kindOf(type?: string): Kind {
  const value = (type || '').toLowerCase();
  if (value === 'charge' || value === 'chargepoint') return 'charge';
  if (value === 'pick' || value === 'drop' || value === 'jobpoint') return 'job';
  if (value === 'park' || value === 'rest' || value === 'standbypoint') return 'standby';
  if (value === 'pullover' || value === 'pulloverpoint') return 'pullover';
  return 'landmark';
}

function priorityOf(kind: Kind) {
  if (kind === 'charge') return 0;
  if (kind === 'standby') return 1;
  if (kind === 'job') return 2;
  if (kind === 'pullover') return 3;
  return 4;
}

function kindColor(kind: Kind, dark: boolean) {
  const light: Record<Kind, number> = {
    charge: 0xb45309,
    job: 0x1d4e89,
    standby: 0x0f766e,
    pullover: 0x6d4fc2,
    landmark: 0x748394
  };
  const night: Record<Kind, number> = {
    charge: 0xf0a53a,
    job: 0x4f9bf0,
    standby: 0x39c2b2,
    pullover: 0xa98bff,
    landmark: 0x9aa8b8
  };
  return (dark ? night : light)[kind];
}

function statusColor(status: string, dark: boolean) {
  if (status === 'Error' || status === 'Fault' || status === 'Estop') return dark ? 0xf0605d : 0xc62828;
  if (status === 'Charging') return dark ? 0xc4b5fd : 0x7c4ddb;
  if (status === 'Running' || status === 'Assigned') return dark ? 0x5aa7ff : 0x1d5fd1;
  if (status === 'Avoiding') return dark ? 0xee6a8c : 0xc12a52;
  if (status === 'WaitingLock') return dark ? 0xe9a23a : 0xc77d12;
  if (status === 'Idle') return dark ? 0x3dbeae : 0x13897f;
  return dark ? 0x93a1b0 : 0x5f6f82;
}

function captionOf(node: Fms.MapNodeDto) {
  const name = (node.name || '').trim();
  if (name && name !== node.id) return name;
  return node.id;
}

function hash01(x: number, y: number) {
  const n = Math.sin(x * 127.1 + y * 311.7) * 43758.5453;
  return n - Math.floor(n);
}

function cubic(t: number, p0: number, p1: number, p2: number, p3: number) {
  const u = 1 - t;
  return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
}

function sharedGeo(key: string, create: () => any) {
  const existing = geometries.get(key);
  if (existing) return existing;
  const geo = create();
  geometries.set(key, geo);
  sharedGeometrySet.add(geo);
  return geo;
}

function ensureShared() {
  if (shared.body) return;
  shared.body = new THREE.MeshStandardMaterial({ roughness: 0.62, metalness: 0.08 });
  shared.deck = new THREE.MeshStandardMaterial({ roughness: 0.55, metalness: 0.06 });
  shared.wheel = new THREE.MeshStandardMaterial({ roughness: 0.9, metalness: 0.02 });
  shared.hub = new THREE.MeshStandardMaterial({ roughness: 0.45, metalness: 0.18 });
  shared.heading = new THREE.MeshStandardMaterial({ roughness: 0.5, metalness: 0.04 });
  shared.pallet = new THREE.MeshStandardMaterial({ roughness: 0.82, metalness: 0 });
  shared.crate = new THREE.MeshStandardMaterial({ roughness: 0.78, metalness: 0 });
  shared.outline = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.95 });
  shared.ring = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.95, side: THREE.DoubleSide, depthWrite: false });
  Object.values(shared).forEach(material => sharedMaterialSet.add(material));
}

function disposeMaterial(material: any) {
  const list = Array.isArray(material) ? material : material ? [material] : [];
  for (const item of list) {
    if (!item || sharedMaterialSet.has(item)) continue;
    item.map?.dispose?.();
    item.dispose?.();
  }
}

function disposeObject(object: any) {
  object.traverse((child: any) => {
    if (child.geometry && !sharedGeometrySet.has(child.geometry)) child.geometry.dispose?.();
    disposeMaterial(child.material);
  });
}

function clearGroup(group: any) {
  const children = [...group.children];
  for (const child of children) {
    group.remove(child);
    disposeObject(child);
  }
}

function toneColor(tone: string, colors: ReturnType<typeof palette>) {
  const table: Record<string, number> = {
    floor: colors.floor,
    'road-fwd': colors.roadFwd,
    'road-back': colors.roadBack,
    'arrow-fwd': colors.arrowFwd,
    'arrow-back': colors.arrowBack,
    task: colors.task,
    selected: colors.selected
  };
  return table[tone];
}

function applyTheme() {
  const dark = themeStore.darkMode;
  const colors = palette(dark);
  if (scene) {
    scene.background = new THREE.Color(colors.bg);
    const far = Math.max(140, mapSpan * 4.5);
    if (!scene.fog) scene.fog = new THREE.Fog(colors.bg, far * 0.2, far);
    else {
      scene.fog.color.setHex(colors.bg);
      scene.fog.near = far * 0.2;
      scene.fog.far = far;
    }
  }
  if (ambient) {
    ambient.color.setHex(colors.ambient);
    ambient.intensity = colors.ambientIntensity;
  }
  if (hemi) {
    hemi.color.setHex(colors.hemiSky);
    hemi.groundColor.setHex(colors.hemiGround);
    hemi.intensity = colors.hemiIntensity;
  }
  if (sun) {
    sun.color.setHex(colors.sun);
    sun.intensity = colors.sunIntensity;
  }
  if (fill) {
    fill.color.setHex(colors.fill);
    fill.intensity = colors.fillIntensity;
  }
  if (floorGrid) {
    const mats = Array.isArray(floorGrid.material) ? floorGrid.material : [floorGrid.material];
    mats[0]?.color?.setHex(colors.gridMajor);
    mats[1]?.color?.setHex(colors.gridMinor);
  }
  shared.body?.color?.setHex(colors.body);
  shared.deck?.color?.setHex(colors.deck);
  shared.heading?.color?.setHex(colors.heading);
  shared.wheel?.color?.setHex(colors.wheel);
  shared.hub?.color?.setHex(colors.hub);
  shared.pallet?.color?.setHex(colors.pallet);
  shared.crate?.color?.setHex(colors.crate);
  shared.outline?.color?.setHex(colors.ring);
  shared.ring?.color?.setHex(colors.ring);
  if (cloudMaterial) {
    cloudMaterial.opacity = colors.cloudOpacity;
    cloudMaterial.color?.setHex(0xffffff);
  }
  writeCloudColors();
  scene?.traverse((obj: any) => {
    const kind = obj.userData?.kind as Kind | undefined;
    if (kind && obj.material?.color) {
      obj.material.color.setHex(kindColor(kind, dark));
      return;
    }
    if (obj.userData?.tone === 'zone' && obj.material?.color) {
      obj.material.color.setHex(obj.userData.busy ? colors.zoneBusy : colors.zoneFree);
      return;
    }
    const tone = obj.userData?.tone as string | undefined;
    const next = tone ? toneColor(tone, colors) : undefined;
    if (next !== undefined && obj.material?.color) obj.material.color.setHex(next);
  });
  for (const group of vehicles.values()) group.userData.sig = '';
  for (const slot of labelSlots) slot.key = '';
}

function vehicleSpec(driveType: string | number | undefined): Spec {
  const raw = String(driveType ?? '').toLowerCase();
  if (raw.includes('single') || raw === '1') return { length: 1.38, width: 0.74, layout: 'steer' };
  if (raw.includes('dual') || raw === '2') return { length: 1.5, width: 0.8, layout: 'dual' };
  if (raw.includes('mecanum') || raw === '3') return { length: 1.08, width: 0.96, layout: 'mecanum' };
  return { length: 1.22, width: 0.82, layout: 'diff' };
}

function chevronGeometry() {
  return sharedGeo('chevron', () => {
    const shape = new THREE.Shape();
    shape.moveTo(0.36, 0);
    shape.lineTo(-0.2, 0.18);
    shape.lineTo(-0.06, 0);
    shape.lineTo(-0.2, -0.18);
    shape.closePath();
    const geom = new THREE.ShapeGeometry(shape);
    geom.rotateX(-Math.PI / 2);
    return geom;
  });
}

function wheelMesh() {
  const tire = new THREE.Mesh(sharedGeo('tire', () => new THREE.CylinderGeometry(0.09, 0.09, 0.07, 14)), shared.wheel);
  tire.rotation.x = Math.PI / 2;
  const hub = new THREE.Mesh(sharedGeo('hub', () => new THREE.CylinderGeometry(0.034, 0.034, 0.078, 10)), shared.hub);
  hub.rotation.x = Math.PI / 2;
  const group = new THREE.Group();
  group.add(tire, hub);
  return group;
}

function createVehicle(driveType: string | number | undefined) {
  ensureShared();
  const spec = vehicleSpec(driveType);
  const group = new THREE.Group();
  const chassis = new THREE.Mesh(
    sharedGeo(`chassis:${spec.layout}`, () => new THREE.BoxGeometry(spec.length * 0.94, 0.13, spec.width * 0.7)),
    shared.body
  );
  chassis.position.set(0, 0.155, 0);
  chassis.name = 'body';
  const deck = new THREE.Mesh(
    sharedGeo(`deck:${spec.layout}`, () => new THREE.BoxGeometry(spec.length * 0.66, 0.05, spec.width * 0.56)),
    shared.deck
  );
  deck.position.set(-spec.length * 0.04, 0.235, 0);
  const mark = new THREE.Mesh(chevronGeometry(), shared.heading);
  mark.position.set(spec.length * 0.1, 0.275, 0);
  mark.scale.set(0.5, 0.5, 0.5);
  const lamp = new THREE.Mesh(
    sharedGeo(`lamp:${spec.layout}`, () => new THREE.BoxGeometry(spec.length * 0.07, 0.03, spec.width * 0.34)),
    new THREE.MeshStandardMaterial({ roughness: 0.4, metalness: 0.04, emissiveIntensity: 0.42 })
  );
  lamp.position.set(spec.length * 0.45, 0.19, 0);
  lamp.name = 'lamp';
  const spots: Array<[number, number]> =
    spec.layout === 'steer'
      ? [[spec.length * 0.36, 0], [-spec.length * 0.3, spec.width * 0.4], [-spec.length * 0.3, -spec.width * 0.4]]
      : [
          [spec.length * 0.32, spec.width * 0.42],
          [spec.length * 0.32, -spec.width * 0.42],
          [-spec.length * 0.32, spec.width * 0.42],
          [-spec.length * 0.32, -spec.width * 0.42]
        ];
  for (const [x, z] of spots) {
    const wheel = wheelMesh();
    wheel.position.set(x, 0.09, z);
    group.add(wheel);
  }
  const cargo = new THREE.Group();
  cargo.name = 'cargo';
  const pallet = new THREE.Mesh(
    sharedGeo(`pallet:${spec.layout}`, () => new THREE.BoxGeometry(spec.length * 0.48, 0.03, spec.width * 0.58)),
    shared.pallet
  );
  pallet.position.set(-spec.length * 0.02, 0.33, 0);
  const crate = new THREE.Mesh(
    sharedGeo(`crate:${spec.layout}`, () => new THREE.BoxGeometry(spec.length * 0.36, 0.18, spec.width * 0.44)),
    shared.crate
  );
  crate.position.set(-spec.length * 0.02, 0.44, 0);
  cargo.add(pallet, crate);
  cargo.visible = false;
  const outline = new THREE.LineSegments(
    sharedGeo(`outline:${spec.layout}`, () => {
      const box = new THREE.BoxGeometry(spec.length * 1.08, 0.5, spec.width * 1.08);
      const edges = new THREE.EdgesGeometry(box);
      box.dispose();
      return edges;
    }),
    shared.outline
  );
  outline.position.y = 0.24;
  outline.name = 'outline';
  outline.visible = false;
  const ring = new THREE.Mesh(sharedGeo('ring', () => new THREE.RingGeometry(0.62, 0.76, 32)), shared.ring);
  ring.rotation.x = -Math.PI / 2;
  ring.position.y = 0.028;
  ring.scale.setScalar(Math.max(spec.length, spec.width) * 0.78);
  ring.name = 'ring';
  ring.visible = false;
  ring.renderOrder = 2;
  group.add(chassis, deck, mark, lamp, cargo, outline, ring);
  return group;
}

function samplePath(path: Fms.MapPathDto, nodes: Map<string, Fms.MapNodeDto>): XZ[] {
  const start = nodes.get(path.startNodeId);
  const end = nodes.get(path.endNodeId);
  if (!start || !end) return [];
  const handles = path.controlPoints || [];
  if (path.pathType === 'CubicBezier' && handles.length >= 2) {
    const approx = Math.hypot(end.x - start.x, end.y - start.y);
    const steps = Math.min(42, Math.max(10, Math.ceil(approx / 0.28)));
    const pts: XZ[] = [];
    for (let step = 0; step <= steps; step += 1) {
      const t = step / steps;
      pts.push({
        x: cubic(t, start.x, handles[0].x, handles[1].x, end.x),
        z: cubic(t, start.y, handles[0].y, handles[1].y, end.y)
      });
    }
    return pts;
  }
  return [
    { x: start.x, z: start.y },
    { x: end.x, z: end.y }
  ];
}

function buildRibbon(pts: XZ[], width: number, y: number) {
  if (pts.length < 2 || width <= 0) return [] as number[];
  const half = width * 0.5;
  const left: XZ[] = [];
  const right: XZ[] = [];
  for (let i = 0; i < pts.length; i += 1) {
    const prev = pts[Math.max(0, i - 1)];
    const next = pts[Math.min(pts.length - 1, i + 1)];
    let dx = next.x - prev.x;
    let dz = next.z - prev.z;
    const len = Math.hypot(dx, dz);
    if (len < 1e-6) {
      dx = 1;
      dz = 0;
    } else {
      dx /= len;
      dz /= len;
    }
    let nx = -dz;
    let nz = dx;
    let scale = 1;
    if (i > 0 && i < pts.length - 1) {
      const p0 = pts[i - 1];
      const p1 = pts[i];
      const p2 = pts[i + 1];
      let ax = p1.x - p0.x;
      let az = p1.z - p0.z;
      let bx = p2.x - p1.x;
      let bz = p2.z - p1.z;
      const la = Math.hypot(ax, az) || 1;
      const lb = Math.hypot(bx, bz) || 1;
      ax /= la;
      az /= la;
      bx /= lb;
      bz /= lb;
      const n1x = -az;
      const n1z = ax;
      nx = n1x - bz;
      nz = n1z + bx;
      const nl = Math.hypot(nx, nz);
      if (nl > 1e-4) {
        nx /= nl;
        nz /= nl;
        const dot = Math.min(1, Math.max(0.25, n1x * nx + n1z * nz));
        scale = Math.min(2.4, 1 / dot);
      }
    }
    const ox = nx * half * scale;
    const oz = nz * half * scale;
    left.push({ x: pts[i].x + ox, z: pts[i].z + oz });
    right.push({ x: pts[i].x - ox, z: pts[i].z - oz });
  }
  const out: number[] = [];
  for (let i = 0; i < pts.length - 1; i += 1) {
    const a = left[i];
    const b = right[i];
    const c = left[i + 1];
    const d = right[i + 1];
    out.push(a.x, y, a.z, b.x, y, b.z, c.x, y, c.z, b.x, y, b.z, d.x, y, d.z, c.x, y, c.z);
  }
  return out;
}

function ribbonMesh(positions: number[], color: number, tone: string, order: number) {
  if (positions.length < 9) return null;
  const geom = new THREE.BufferGeometry();
  geom.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  const mat = new THREE.MeshBasicMaterial({
    color,
    transparent: true,
    opacity: tone === 'task' || tone === 'selected' ? 0.96 : 0.9,
    depthWrite: false,
    side: THREE.DoubleSide,
    polygonOffset: true,
    polygonOffsetFactor: -1,
    polygonOffsetUnits: -1
  });
  const mesh = new THREE.Mesh(geom, mat);
  mesh.userData.tone = tone;
  mesh.renderOrder = order;
  mesh.frustumCulled = false;
  return mesh;
}

function collectArrows(pts: XZ[], width: number, out: Array<{ x: number; z: number; angle: number; scale: number }>) {
  const stride = 5;
  const scale = Math.min(0.9, Math.max(0.34, width * 0.42));
  const before = out.length;
  let cursor = stride * 0.65;
  for (let i = 1; i < pts.length; i += 1) {
    const a = pts[i - 1];
    const b = pts[i];
    const dx = b.x - a.x;
    const dz = b.z - a.z;
    const seg = Math.hypot(dx, dz);
    if (seg < 1e-4) continue;
    const ux = dx / seg;
    const uz = dz / seg;
    while (cursor <= seg) {
      out.push({ x: a.x + ux * cursor, z: a.z + uz * cursor, angle: Math.atan2(-uz, ux), scale });
      cursor += stride;
      if (out.length > 4000) return;
    }
    cursor -= seg;
  }
  if (out.length === before && pts.length >= 2) {
    const mid = pts[Math.floor(pts.length / 2)];
    const prev = pts[Math.max(0, Math.floor(pts.length / 2) - 1)];
    const dx = mid.x - prev.x || pts[pts.length - 1].x - pts[0].x;
    const dz = mid.z - prev.z || pts[pts.length - 1].z - pts[0].z;
    if (Math.hypot(pts[pts.length - 1].x - pts[0].x, pts[pts.length - 1].z - pts[0].z) > 0.6) {
      out.push({ x: mid.x, z: mid.z, angle: Math.atan2(-dz, dx), scale });
    }
  }
}

function arrowMesh(arrows: Array<{ x: number; z: number; angle: number; scale: number }>, color: number, tone: string) {
  if (!arrows.length) return null;
  const mesh = new THREE.InstancedMesh(
    chevronGeometry(),
    new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.95, depthWrite: false, side: THREE.DoubleSide }),
    arrows.length
  );
  arrows.forEach((arrow, index) => {
    dummy.position.set(arrow.x, 0.055, arrow.z);
    dummy.rotation.set(0, arrow.angle, 0);
    dummy.scale.set(arrow.scale, arrow.scale, arrow.scale);
    dummy.updateMatrix();
    mesh.setMatrixAt(index, dummy.matrix);
  });
  dummy.scale.set(1, 1, 1);
  mesh.instanceMatrix.needsUpdate = true;
  mesh.userData.tone = tone;
  mesh.frustumCulled = false;
  mesh.renderOrder = 2;
  return mesh;
}

function nodeGeometry(kind: Kind) {
  return sharedGeo(`node:${kind}`, () => {
    if (kind === 'charge') {
      const geom = new THREE.CylinderGeometry(0.15, 0.17, 0.1, 6);
      geom.translate(0, 0.05, 0);
      return geom;
    }
    if (kind === 'job') {
      const geom = new THREE.BoxGeometry(0.26, 0.08, 0.26);
      geom.translate(0, 0.04, 0);
      return geom;
    }
    if (kind === 'standby') {
      const geom = new THREE.OctahedronGeometry(0.15, 0);
      geom.scale(1, 0.5, 1);
      geom.translate(0, 0.08, 0);
      return geom;
    }
    if (kind === 'pullover') {
      const geom = new THREE.ConeGeometry(0.14, 0.16, 3);
      geom.translate(0, 0.08, 0);
      return geom;
    }
    const geom = new THREE.CylinderGeometry(0.065, 0.065, 0.03, 10);
    geom.translate(0, 0.015, 0);
    return geom;
  });
}

function placeGrid(bounds: Bounds) {
  if (!scene || !Number.isFinite(bounds.minX)) return;
  if (floorGrid) {
    scene.remove(floorGrid);
    disposeObject(floorGrid);
    floorGrid = null;
  }
  const spanX = Math.max(8, bounds.maxX - bounds.minX);
  const spanY = Math.max(8, bounds.maxY - bounds.minY);
  const size = Math.max(spanX, spanY) * 1.45;
  const divisions = Math.min(64, Math.max(8, Math.round(size / 4)));
  const colors = palette(themeStore.darkMode);
  floorGrid = new THREE.GridHelper(size, divisions, colors.gridMajor, colors.gridMinor);
  floorGrid.position.set((bounds.minX + bounds.maxX) / 2, 0.004, (bounds.minY + bounds.maxY) / 2);
  const mats = Array.isArray(floorGrid.material) ? floorGrid.material : [floorGrid.material];
  mats.forEach((material: any) => {
    material.transparent = true;
    material.opacity = 0.7;
  });
  scene.add(floorGrid);
  if (floor) {
    floor.scale.set(size, size, 1);
    floor.position.set((bounds.minX + bounds.maxX) / 2, -0.02, (bounds.minY + bounds.maxY) / 2);
  }
}

function maybeFrame(bounds: Bounds) {
  if (!Number.isFinite(bounds.minX) || !camera || !orbit) return;
  const span = Math.max(12, bounds.maxX - bounds.minX, bounds.maxY - bounds.minY);
  mapSpan = span;
  const cx = (bounds.minX + bounds.maxX) / 2;
  const cz = (bounds.minY + bounds.maxY) / 2;
  camera.near = 0.05;
  camera.far = Math.max(500, span * 12);
  camera.updateProjectionMatrix();
  orbit.minDistance = Math.max(1.2, Math.min(5, span * 0.015));
  orbit.maxDistance = Math.max(36, span * 2.8);
  applyTheme();
  const key = `${bounds.minX.toFixed(2)}|${bounds.maxX.toFixed(2)}|${bounds.minY.toFixed(2)}|${bounds.maxY.toFixed(2)}`;
  if (key === framedKey) return;
  framedKey = key;
  camera.position.set(cx + span * 0.04, span * 0.58, cz + span * 0.78);
  orbit.target.set(cx, 0, cz);
  orbit.update();
}

function writeCloudColors() {
  if (!cloudGeometry) return;
  const colorAttr = cloudGeometry.getAttribute('color');
  const shades = cloudGeometry.userData.shades as Float32Array | undefined;
  const warm = cloudGeometry.userData.warm as Float32Array | undefined;
  if (!colorAttr || !shades || !warm) return;
  const dark = themeStore.darkMode;
  const cool = dark ? [0.52, 0.62, 0.7] : [0.32, 0.4, 0.48];
  const hot = dark ? [0.66, 0.74, 0.68] : [0.5, 0.54, 0.45];
  for (let i = 0; i < shades.length; i += 1) {
    const shade = shades[i];
    const mix = warm[i];
    colorAttr.setXYZ(
      i,
      (cool[0] * (1 - mix) + hot[0] * mix) * shade,
      (cool[1] * (1 - mix) + hot[1] * mix) * shade,
      (cool[2] * (1 - mix) + hot[2] * mix) * shade
    );
  }
  colorAttr.needsUpdate = true;
}

function buildStatic() {
  if (!scene || !staticRoot) return;
  clearGroup(staticRoot);
  nodeMeshes.length = 0;
  nodeRecs.length = 0;
  nodeById.clear();
  cloudMaterial = null;
  cloudGeometry = null;
  const graph = props.graph;
  if (!graph?.nodes?.length) {
    rebuildOverlays();
    return;
  }

  if (props.raster?.url) {
    const texture = new THREE.TextureLoader().load(props.raster.url);
    texture.colorSpace = THREE.SRGBColorSpace;
    const plane = new THREE.Mesh(
      new THREE.PlaneGeometry(props.raster.widthMeters, props.raster.heightMeters),
      new THREE.MeshBasicMaterial({ map: texture, transparent: true, opacity: 0.94 })
    );
    plane.rotation.x = -Math.PI / 2;
    plane.position.set(props.raster.originX + props.raster.widthMeters / 2, 0.012, props.raster.topY - props.raster.heightMeters / 2);
    staticRoot.add(plane);
  }

  const nodes = new Map(graph.nodes.map(node => [node.id, node]));
  const forward: number[] = [];
  const backward: number[] = [];
  const forwardArrows: Array<{ x: number; z: number; angle: number; scale: number }> = [];
  const backwardArrows: Array<{ x: number; z: number; angle: number; scale: number }> = [];
  for (const path of graph.paths || []) {
    const pts = samplePath(path, nodes);
    if (pts.length < 2) continue;
    const width = Math.min(2.4, Math.max(0.45, path.roadWidth || 1.2));
    const backwardPath = path.direction === 'backward';
    const positions = buildRibbon(pts, width, backwardPath ? 0.036 : 0.03);
    const bucket = backwardPath ? backward : forward;
    for (let i = 0; i < positions.length; i += 1) bucket.push(positions[i]);
    collectArrows(pts, width, backwardPath ? backwardArrows : forwardArrows);
  }
  const colors = palette(themeStore.darkMode);
  const fwd = ribbonMesh(forward, colors.roadFwd, 'road-fwd', 1);
  const back = ribbonMesh(backward, colors.roadBack, 'road-back', 1);
  const fwdArrows = arrowMesh(forwardArrows, colors.arrowFwd, 'arrow-fwd');
  const backArrows = arrowMesh(backwardArrows, colors.arrowBack, 'arrow-back');
  if (fwd) staticRoot.add(fwd);
  if (back) staticRoot.add(back);
  if (fwdArrows) staticRoot.add(fwdArrows);
  if (backArrows) staticRoot.add(backArrows);

  const bounds = graph.nodes.reduce(
    (acc, node) => ({
      minX: Math.min(acc.minX, node.x),
      maxX: Math.max(acc.maxX, node.x),
      minY: Math.min(acc.minY, node.y),
      maxY: Math.max(acc.maxY, node.y)
    }),
    { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity }
  );

  const cloud = props.pointCloud || [];
  if (cloud.length) {
    const stride = Math.max(1, Math.ceil(cloud.length / 40000));
    const count = Math.ceil(cloud.length / stride);
    const positions = new Float32Array(count * 3);
    const colorArray = new Float32Array(count * 3);
    const shades = new Float32Array(count);
    const warm = new Float32Array(count);
    const spanY = Math.max(1e-3, bounds.maxY - bounds.minY);
    let write = 0;
    let slot = 0;
    for (let index = 0; index < cloud.length; index += stride) {
      const point = cloud[index];
      const lift = hash01(point.x, point.y);
      const along = Number.isFinite(bounds.minY) ? (point.y - bounds.minY) / spanY : 0.5;
      shades[slot] = 0.82 + 0.18 * (0.6 * along + 0.4 * lift);
      warm[slot] = lift;
      positions[write] = point.x;
      positions[write + 1] = 0.02 + lift * 0.035;
      positions[write + 2] = point.y;
      write += 3;
      slot += 1;
    }
    const geom = new THREE.BufferGeometry();
    geom.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geom.setAttribute('color', new THREE.BufferAttribute(colorArray, 3));
    geom.userData.shades = shades;
    geom.userData.warm = warm;
    cloudGeometry = geom;
    cloudMaterial = new THREE.PointsMaterial({
      size: 0.08,
      vertexColors: true,
      transparent: true,
      opacity: colors.cloudOpacity,
      sizeAttenuation: true,
      depthWrite: false
    });
    const points = new THREE.Points(geom, cloudMaterial);
    points.frustumCulled = false;
    staticRoot.add(points);
    writeCloudColors();
  }

  const zones = props.mutexZones?.length ? props.mutexZones : graph.mutexZones || [];
  for (const zone of zones) {
    const polygon = zone.polygon;
    if (!polygon || polygon.length < 3) continue;
    const shape = new THREE.Shape(polygon.map(point => new THREE.Vector2(point.x, point.y)));
    const busy = (zone.currentOccupantVehicleIds?.length || 0) > 0;
    const mesh = new THREE.Mesh(
      new THREE.ShapeGeometry(shape),
      new THREE.MeshBasicMaterial({
        color: busy ? colors.zoneBusy : colors.zoneFree,
        transparent: true,
        opacity: 0.18,
        depthWrite: false,
        side: THREE.DoubleSide
      })
    );
    mesh.rotation.x = Math.PI / 2;
    mesh.position.y = 0.02;
    mesh.userData.tone = 'zone';
    mesh.userData.zoneId = zone.zoneId;
    mesh.userData.busy = busy;
    staticRoot.add(mesh);
  }

  const groups = new Map<Kind, number[]>();
  graph.nodes.forEach((node, index) => {
    const kind = kindOf(node.type);
    const list = groups.get(kind) || [];
    list.push(index);
    groups.set(kind, list);
    const rec: NodeRec = {
      id: node.id,
      caption: captionOf(node),
      x: node.x,
      z: node.y,
      kind,
      priority: priorityOf(kind)
    };
    nodeRecs.push(rec);
    nodeById.set(node.id, rec);
  });
  for (const [kind, indexes] of groups) {
    const material = new THREE.MeshStandardMaterial({
      color: kindColor(kind, themeStore.darkMode),
      roughness: 0.72,
      metalness: 0.04
    });
    const mesh = new THREE.InstancedMesh(nodeGeometry(kind), material, indexes.length);
    const ids: string[] = [];
    indexes.forEach((nodeIndex, slot) => {
      const node = graph.nodes[nodeIndex];
      ids.push(node.id);
      dummy.position.set(node.x, 0, node.y);
      dummy.rotation.set(0, 0, 0);
      dummy.scale.set(1, 1, 1);
      dummy.updateMatrix();
      mesh.setMatrixAt(slot, dummy.matrix);
    });
    mesh.instanceMatrix.needsUpdate = true;
    mesh.userData.kind = kind;
    mesh.userData.nodeIds = ids;
    mesh.frustumCulled = false;
    staticRoot.add(mesh);
    nodeMeshes.push(mesh);
  }

  placeGrid(bounds);
  maybeFrame(bounds);
  rebuildOverlays();
}

function rebuildOverlays() {
  if (!overlayRoot) return;
  clearGroup(overlayRoot);
  const graph = props.graph;
  if (!graph?.nodes?.length) return;
  const colors = palette(themeStore.darkMode);
  const nodes = new Map(graph.nodes.map(node => [node.id, node]));
  if (props.selectedPathId) {
    const path = (graph.paths || []).find(item => item.id === props.selectedPathId);
    if (path) {
      const width = Math.min(1.1, Math.max(0.35, (path.roadWidth || 1.2) * 0.5));
      const mesh = ribbonMesh(buildRibbon(samplePath(path, nodes), width, 0.09), colors.selected, 'selected', 4);
      if (mesh) overlayRoot.add(mesh);
    }
  }
  const taskPositions: number[] = [];
  for (const order of props.orders || []) {
    const route = order.route;
    if (!route || route.length < 2) continue;
    const pts: XZ[] = [];
    for (const pair of route) {
      if (!pair || pair.length < 2 || !Number.isFinite(pair[0]) || !Number.isFinite(pair[1])) continue;
      pts.push({ x: pair[0], z: pair[1] });
    }
    const chunk = buildRibbon(pts, 0.55, 0.075);
    for (let i = 0; i < chunk.length; i += 1) taskPositions.push(chunk[i]);
  }
  const task = ribbonMesh(taskPositions, colors.task, 'task', 3);
  if (task) overlayRoot.add(task);
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  const radius = Math.min(r, h / 2, w / 2);
  ctx.beginPath();
  ctx.moveTo(x + radius, y);
  ctx.arcTo(x + w, y, x + w, y + h, radius);
  ctx.arcTo(x + w, y + h, x, y + h, radius);
  ctx.arcTo(x, y + h, x, y, radius);
  ctx.arcTo(x, y, x + w, y, radius);
  ctx.closePath();
}

function textureFor(text: string) {
  const dark = themeStore.darkMode;
  const key = `${dark ? 'd' : 'l'}|${text}`;
  const cached = labelTextures.get(key);
  if (cached) {
    labelTextures.delete(key);
    labelTextures.set(key, cached);
    return { key, tex: cached.tex, aspect: cached.aspect };
  }
  const fontSize = 42;
  const padX = 14;
  const padY = 8;
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  if (!ctx) return { key, tex: null as any, aspect: 2 };
  const font = `600 ${fontSize}px "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif`;
  ctx.font = font;
  const textWidth = Math.ceil(ctx.measureText(text).width);
  const width = Math.max(24, textWidth + padX * 2);
  const height = fontSize + padY * 2;
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = Math.ceil(width * dpr);
  canvas.height = Math.ceil(height * dpr);
  ctx.scale(dpr, dpr);
  ctx.font = font;
  ctx.clearRect(0, 0, width, height);
  roundRect(ctx, 1, 1, width - 2, height - 2, 8);
  ctx.fillStyle = dark ? 'rgba(14,19,24,0.9)' : 'rgba(255,255,255,0.93)';
  ctx.fill();
  ctx.lineWidth = 1.5;
  ctx.strokeStyle = dark ? 'rgba(244,247,251,0.4)' : 'rgba(27,39,51,0.22)';
  ctx.stroke();
  ctx.fillStyle = dark ? '#f4f7fb' : '#1b2733';
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, padX, height / 2 + 0.5);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.minFilter = THREE.LinearFilter;
  tex.magFilter = THREE.LinearFilter;
  tex.generateMipmaps = false;
  const record = { tex, aspect: width / height };
  labelTextures.set(key, record);
  return { key, tex, aspect: record.aspect };
}

function trimLabelTextures() {
  if (labelTextures.size <= 200) return;
  for (const key of [...labelTextures.keys()]) {
    if (labelTextures.size <= 160) break;
    if (liveLabelKeys.has(key)) continue;
    labelTextures.get(key)?.tex.dispose();
    labelTextures.delete(key);
  }
}

function ensureLabelSlot(index: number) {
  const existing = labelSlots[index];
  if (existing) return existing;
  const material = new THREE.SpriteMaterial({
    transparent: true,
    depthWrite: false,
    depthTest: true,
    fog: false,
    sizeAttenuation: true
  });
  const sprite = new THREE.Sprite(material);
  sprite.center.set(0.5, 0);
  sprite.renderOrder = 20;
  sprite.raycast = () => {};
  labelRoot?.add(sprite);
  const slot = { sprite, key: '' };
  labelSlots[index] = slot;
  return slot;
}

function updateLabels() {
  if (!camera || !labelRoot) return;
  camera.updateMatrixWorld();
  proj.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
  frustum.setFromProjectionMatrix(proj);
  const stationRange = Math.max(18, mapSpan * 0.36);
  const landmarkRange = Math.max(8, mapSpan * 0.13);
  const selectedId = props.selectedNodeId || '';
  let count = 0;
  for (const rec of nodeRecs) {
    world.set(rec.x, 0.45, rec.z);
    if (!frustum.containsPoint(world)) continue;
    const dist = world.distanceTo(camera.position);
    const selected = rec.id === selectedId;
    if (!selected && dist > (rec.kind === 'landmark' ? landmarkRange : stationRange)) continue;
    candRec[count] = rec;
    candDist[count] = dist;
    candSel[count] = selected ? 1 : 0;
    count += 1;
  }
  candOrder.length = count;
  for (let i = 0; i < count; i += 1) candOrder[i] = i;
  candOrder.sort((i, j) => candSel[j] - candSel[i] || candRec[i].priority - candRec[j].priority || candDist[i] - candDist[j]);
  liveLabelKeys.clear();
  let placed = 0;
  placedX.length = 0;
  placedY.length = 0;
  for (let n = 0; n < candOrder.length && placed < MAX_LABELS; n += 1) {
    const index = candOrder[n];
    const rec = candRec[index];
    const selected = candSel[index] === 1;
    ndc.set(rec.x, 0.45, rec.z).project(camera);
    if (!selected) {
      let overlap = false;
      for (let p = 0; p < placedX.length; p += 1) {
        const dx = placedX[p] - ndc.x;
        const dy = placedY[p] - ndc.y;
        if (dx * dx + dy * dy < 0.0075) {
          overlap = true;
          break;
        }
      }
      if (overlap) continue;
    }
    const made = textureFor(rec.caption);
    if (!made.tex) continue;
    const slot = ensureLabelSlot(placed);
    slot.sprite.visible = true;
    slot.sprite.position.set(rec.x, 0.42, rec.z);
    if (slot.key !== made.key) {
      slot.sprite.material.map = made.tex;
      slot.sprite.material.needsUpdate = true;
      slot.sprite.userData.aspect = made.aspect;
      slot.key = made.key;
    }
    const height = THREE.MathUtils.clamp(Math.max(candDist[index], 1) * 0.022, 0.32, 1.45);
    const aspect = slot.sprite.userData.aspect || made.aspect;
    slot.sprite.scale.set(height * aspect, height, 1);
    placedX.push(ndc.x);
    placedY.push(ndc.y);
    liveLabelKeys.add(made.key);
    placed += 1;
  }
  for (let i = placed; i < labelSlots.length; i += 1) labelSlots[i].sprite.visible = false;
  trimLabelTextures();
}

function updateCloudSize() {
  if (!cloudMaterial || !camera || !orbit) return;
  const dist = camera.position.distanceTo(orbit.target);
  cloudMaterial.size = THREE.MathUtils.clamp(0.055 + dist * 0.0011, 0.05, 0.2);
}

function syncZoneOccupancy() {
  if (!staticRoot || !props.mutexZones?.length) return;
  const colors = palette(themeStore.darkMode);
  const busy = new Map(props.mutexZones.map(zone => [zone.zoneId, (zone.currentOccupantVehicleIds?.length || 0) > 0]));
  staticRoot.traverse((obj: any) => {
    if (obj.userData?.tone !== 'zone') return;
    const next = busy.get(obj.userData.zoneId);
    if (next === undefined || next === obj.userData.busy) return;
    obj.userData.busy = next;
    obj.material?.color?.setHex(next ? colors.zoneBusy : colors.zoneFree);
  });
}

function syncSelectRing() {
  if (!nodeRing) return;
  const rec = props.selectedNodeId ? nodeById.get(props.selectedNodeId) : undefined;
  if (!rec) {
    nodeRing.visible = false;
    return;
  }
  nodeRing.visible = true;
  nodeRing.position.set(rec.x, 0.045, rec.z);
  const span = rec.kind === 'landmark' ? 0.55 : 0.85;
  nodeRing.scale.set(span, span, span);
}

function syncVehicles() {
  if (!scene) return;
  ensureShared();
  const live = new Set<string>();
  const dark = themeStore.darkMode;
  for (const vehicle of props.vehicles || []) {
    const pose = vehicle.pose;
    if (!pose || !Number.isFinite(pose.x) || !Number.isFinite(pose.y)) continue;
    live.add(vehicle.id);
    let group = vehicles.get(vehicle.id);
    if (!group) {
      group = createVehicle(vehicle.driveType);
      group.userData.vehicleId = vehicle.id;
      scene.add(group);
      vehicles.set(vehicle.id, group);
    }
    group.position.set(pose.x, 0, pose.y);
    group.rotation.y = -Number(pose.theta || 0);
    const selected = vehicle.id === props.selectedVehicleId;
    const sig = `${vehicle.status}|${vehicle.holdingPayload ? 1 : 0}|${selected ? 1 : 0}|${dark ? 1 : 0}`;
    if (group.userData.sig !== sig) {
      group.userData.sig = sig;
      const lamp = group.getObjectByName('lamp');
      const color = statusColor(vehicle.status, dark);
      if (lamp?.material) {
        lamp.material.color.setHex(color);
        lamp.material.emissive.setHex(color);
      }
      const cargo = group.getObjectByName('cargo');
      if (cargo) cargo.visible = Boolean(vehicle.holdingPayload);
      const outline = group.getObjectByName('outline');
      if (outline) outline.visible = selected;
      const ring = group.getObjectByName('ring');
      if (ring) ring.visible = selected;
    }
  }
  for (const [id, group] of vehicles) {
    if (live.has(id)) continue;
    const lamp = group.getObjectByName('lamp');
    lamp?.material?.dispose?.();
    scene.remove(group);
    vehicles.delete(id);
  }
}

function vehicleIdFrom(object: any) {
  let current = object;
  while (current) {
    if (current.userData?.vehicleId) return current.userData.vehicleId as string;
    current = current.parent;
  }
  return null;
}

function pick(event: PointerEvent) {
  if (!host.value || !camera) return;
  const rect = host.value.getBoundingClientRect();
  if (rect.width < 2 || rect.height < 2) return;
  pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const vehicleHits = raycaster.intersectObjects([...vehicles.values()], true);
  if (vehicleHits.length) {
    const id = vehicleIdFrom(vehicleHits[0].object);
    if (id) {
      emit('select-vehicle', id);
      return;
    }
  }
  const nodeHits = raycaster.intersectObjects(nodeMeshes, false);
  const hit = nodeHits[0];
  const ids = hit?.object?.userData?.nodeIds as string[] | undefined;
  if (hit && typeof hit.instanceId === 'number' && ids?.[hit.instanceId]) {
    emit('select-node', ids[hit.instanceId]);
  }
}

function onPointerDown(event: PointerEvent) {
  pressButton = event.button;
  pressX = event.clientX;
  pressY = event.clientY;
}

function onPointerUp(event: PointerEvent) {
  if (event.button !== 0 || pressButton !== 0) return;
  const dx = event.clientX - pressX;
  const dy = event.clientY - pressY;
  if (dx * dx + dy * dy > 25) return;
  pick(event);
}

function onWheel(event: WheelEvent) {
  event.preventDefault();
  event.stopPropagation();
}

function configureOrbit() {
  if (!orbit) return;
  orbit.enableDamping = true;
  orbit.dampingFactor = 0.085;
  orbit.enablePan = true;
  orbit.enableRotate = true;
  orbit.enableZoom = true;
  orbit.screenSpacePanning = true;
  orbit.zoomToCursor = true;
  orbit.rotateSpeed = 0.7;
  orbit.zoomSpeed = 0.9;
  orbit.panSpeed = 0.85;
  orbit.minPolarAngle = 0.16;
  orbit.maxPolarAngle = Math.PI / 2.04;
  orbit.minDistance = 1.4;
  orbit.maxDistance = 800;
  orbit.mouseButtons.LEFT = THREE.MOUSE.ROTATE;
  orbit.mouseButtons.MIDDLE = THREE.MOUSE.PAN;
  orbit.mouseButtons.RIGHT = THREE.MOUSE.PAN;
}

function resize() {
  if (!renderer || !camera || !host.value) return;
  const width = host.value.clientWidth;
  const height = host.value.clientHeight;
  if (width < 2 || height < 2) return;
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  renderer.setSize(width, height);
  renderer.domElement.style.pointerEvents = 'none';
  renderer.domElement.style.display = 'block';
}

function tick() {
  frame = requestAnimationFrame(tick);
  if (!renderer || !scene || !camera) return;
  orbit?.update();
  syncVehicles();
  syncZoneOccupancy();
  syncSelectRing();
  updateLabels();
  updateCloudSize();
  renderer.render(scene, camera);
}

onMounted(() => {
  const el = host.value;
  if (!el) return;
  ensureShared();
  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(48, 1, 0.05, 4000);
  camera.position.set(18, 22, 24);
  renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.NoToneMapping;
  el.appendChild(renderer.domElement);
  renderer.domElement.style.pointerEvents = 'none';
  renderer.domElement.style.display = 'block';
  orbit = new OrbitControls(camera, el);
  configureOrbit();
  ambient = new THREE.AmbientLight(0xffffff, 0.7);
  hemi = new THREE.HemisphereLight(0xffffff, 0xcccccc, 0.35);
  sun = new THREE.DirectionalLight(0xffffff, 0.7);
  sun.position.set(28, 64, 22);
  fill = new THREE.DirectionalLight(0xdbe7f2, 0.2);
  fill.position.set(-26, 34, -16);
  scene.add(ambient, hemi, sun, fill);
  floor = new THREE.Mesh(
    new THREE.PlaneGeometry(1, 1),
    new THREE.MeshStandardMaterial({ roughness: 1, metalness: 0 })
  );
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -0.02;
  floor.userData.tone = 'floor';
  scene.add(floor);
  staticRoot = new THREE.Group();
  overlayRoot = new THREE.Group();
  labelRoot = new THREE.Group();
  scene.add(staticRoot, overlayRoot, labelRoot);
  nodeRing = new THREE.Mesh(sharedGeo('node-ring', () => new THREE.RingGeometry(0.28, 0.4, 28)), shared.ring);
  nodeRing.rotation.x = -Math.PI / 2;
  nodeRing.position.y = 0.045;
  nodeRing.visible = false;
  nodeRing.renderOrder = 2;
  scene.add(nodeRing);
  el.addEventListener('wheel', onWheel, { passive: false });
  el.addEventListener('pointerdown', onPointerDown);
  el.addEventListener('pointerup', onPointerUp);
  applyTheme();
  buildStatic();
  resize();
  resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(el);
  tick();
});

watch(
  () => [props.graph, props.raster?.url, props.pointCloud],
  () => buildStatic()
);

watch(
  () => [props.selectedPathId, props.orders],
  () => rebuildOverlays(),
  { deep: true }
);

watch(
  () => themeStore.darkMode,
  () => applyTheme()
);

onBeforeUnmount(() => {
  cancelAnimationFrame(frame);
  resizeObserver?.disconnect();
  const el = host.value;
  el?.removeEventListener('wheel', onWheel);
  el?.removeEventListener('pointerdown', onPointerDown);
  el?.removeEventListener('pointerup', onPointerUp);
  if (staticRoot) clearGroup(staticRoot);
  if (overlayRoot) clearGroup(overlayRoot);
  for (const group of vehicles.values()) {
    group.getObjectByName('lamp')?.material?.dispose?.();
    scene?.remove(group);
  }
  vehicles.clear();
  for (const slot of labelSlots) slot.sprite.material?.dispose?.();
  for (const item of labelTextures.values()) item.tex.dispose();
  labelTextures.clear();
  if (floor && scene) {
    scene.remove(floor);
    disposeObject(floor);
  }
  if (floorGrid && scene) {
    scene.remove(floorGrid);
    disposeObject(floorGrid);
  }
  geometries.forEach(geo => geo.dispose?.());
  geometries.clear();
  Object.values(shared).forEach(material => material.dispose?.());
  orbit?.dispose();
  renderer?.dispose();
  renderer?.domElement.remove();
});
</script>

<template>
  <div ref="host" class="twin-host" />
</template>

<style scoped>
.twin-host {
  width: 100%;
  height: 100%;
  touch-action: none;
  overscroll-behavior: none;
  cursor: grab;
  user-select: none;
}

.twin-host:active {
  cursor: grabbing;
}
</style>
