<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, shallowRef, watch } from 'vue';
import { Application, Container, Graphics, Sprite, Text, Texture } from 'pixi.js';
import type { TextStyleOptions } from 'pixi.js';
import { useThemeStore } from '@/store/modules/theme';
import { orderStatusLabel, vehicleCaption, vehicleStatusLabel } from '@/utils/fms-display';
import MapContextMenu from './map/MapContextMenu.vue';
import type { MapMenuItem } from './map/MapContextMenu.vue';
import MapMiniMap from './map/MapMiniMap.vue';
import MapSearchBox from './map/MapSearchBox.vue';
import MapShortcutsHelp from './map/MapShortcutsHelp.vue';
import { BOOKMARK_LIMIT, loadBookmarks, makeBookmarkId, saveBookmarks } from './map/bookmarks';
import { Camera, clamp, normalizeAngle, shortestAngle } from './map/camera';
import { FOCUS_PX_PER_M, MAX_ZOOM, MIN_ZOOM, SCALE, TIER_NAMES, lodTier } from './map/constants';
import type { Bounds, CameraState, FlyTarget, MapBookmark, PickResult, ViewPreset } from './map/constants';
import {
  boundsContain,
  boundsIntersect,
  boundsOfPoints,
  distToSegmentSq,
  expandBounds,
  offsetPolyline,
  pointInPolygon,
  pointsAtDistances,
  polygonCentroid,
  polylineLength,
  sampleBezier
} from './map/geometry';
import type { Pt } from './map/geometry';
import { batteryColor, cssColor, getPalette, vehicleColor } from './map/palette';
import type { Palette } from './map/palette';
import { SEARCH_KIND_LABELS } from './map/search';
import type { SearchItem } from './map/search';
import { STATION_LABELS, drawStation, stationKindOf, stationRadius } from './map/stations';
import type { StationKind } from './map/stations';
import { drawBatteryRing, drawStatusBadge, drawVehicleBody, vehicleMetrics } from './map/vehicle-shape';
import type { VehicleDrawContext } from './map/vehicle-shape';

const themeStore = useThemeStore();

const props = withDefaults(
  defineProps<{
    graph?: Fms.MapGraphDto | null;
    vehicles?: Fms.VehicleDto[];
    mutexZones?: Fms.MutexZoneDto[];
    /** 在途任务：用于在地图上画出车辆剩余路线与目的地 */
    orders?: Fms.OrderDto[];
    /** 冲突告警：在地图上标出冲突位置 */
    conflicts?: Fms.ConflictAlertDto[];
    pointCloud?: Fms.Point2D[];
    raster?: {
      url: string;
      originX: number;
      topY: number;
      widthMeters: number;
      heightMeters: number;
    } | null;
    selectedVehicleId?: string | null;
    selectedNodeId?: string | null;
    selectedPathId?: string | null;
    layerVisibility?: {
      nodes: boolean;
      straightPaths: boolean;
      bezierPaths: boolean;
      arrows: boolean;
      mutexZones: boolean;
      lidarPoints: boolean;
      raster: boolean;
      stationLabels: boolean;
    };
    hudInset?: {
      left?: number;
      right?: number;
      bottom?: number;
      top?: number;
    };
    /** 左上角搜索框（站点、车辆、任务流水号） */
    showSearch?: boolean;
    /** 鹰眼小地图 */
    showNavigator?: boolean;
    /** 右上角视角预设、书签、快捷键 */
    showToolbar?: boolean;
    /** 工具栏里的巡航开关 */
    showCruise?: boolean;
    /** 巡航开关，可用 v-model:cruise 绑定 */
    cruise?: boolean;
    /** 点击空白或按 Esc 时清除选中 */
    deselectable?: boolean;
    /** 右键菜单里提供「从此处新建导航任务」 */
    enableTaskMenu?: boolean;
    /** 启用键盘快捷键（仅在鼠标停留或聚焦于地图时生效） */
    keyboard?: boolean;
  }>(),
  {
    graph: null,
    vehicles: () => [],
    mutexZones: () => [],
    orders: () => [],
    conflicts: () => [],
    pointCloud: () => [],
    raster: null,
    selectedVehicleId: null,
    selectedNodeId: null,
    selectedPathId: null,
    layerVisibility: () => ({
      nodes: true,
      straightPaths: true,
      bezierPaths: true,
      arrows: true,
      mutexZones: true,
      lidarPoints: false,
      raster: true,
      stationLabels: true
    }),
    hudInset: () => ({}),
    showSearch: false,
    showNavigator: false,
    showToolbar: false,
    showCruise: false,
    cruise: false,
    deselectable: false,
    enableTaskMenu: false,
    keyboard: true
  }
);

const emit = defineEmits<{
  (e: 'select-vehicle', id: string | null): void;
  (e: 'select-node', id: string | null): void;
  (e: 'select-path', id: string | null): void;
  (e: 'request-create-task', payload: { targetNodeId: string | null; x: number; y: number }): void;
  (e: 'view-order', payload: { vehicleId: string; orderId: string | null }): void;
  (e: 'follow-change', id: string | null): void;
  (e: 'update:cruise', value: boolean): void;
}>();

const hud = computed(() => ({
  left: props.hudInset?.left ?? 16,
  right: props.hudInset?.right ?? 16,
  bottom: props.hudInset?.bottom ?? 16,
  top: props.hudInset?.top ?? 16
}));

// ───────────────────────────── 相机与可视状态 ─────────────────────────────

const rootRef = ref<HTMLDivElement | null>(null);
const hostRef = ref<HTMLDivElement | null>(null);
const searchRef = ref<InstanceType<typeof MapSearchBox> | null>(null);

const camera = new Camera();
const cameraView = shallowRef<CameraState>(camera.state());
const viewQuad = shallowRef<number[]>([]);
const cursorWorld = ref({ x: 0, y: 0 });
const hovering = ref(false);
const fittedScale = ref(1);

interface HoverState {
  kind: 'vehicle' | 'node';
  id: string;
  sx: number;
  sy: number;
}

const hover = shallowRef<HoverState | null>(null);
const followId = ref<string | null>(null);
const followHeading = ref(false);
const activePreset = ref<ViewPreset | null>(null);

const pxPerMeter = computed(() => cameraView.value.scale * SCALE);
const tierName = computed(() => TIER_NAMES[lodTier(pxPerMeter.value)]);
const displayZoom = computed(() => Math.round((cameraView.value.scale / (fittedScale.value || 1)) * 100));
const rotationDeg = computed(() => (cameraView.value.rotation * 180) / Math.PI);
const northUp = computed(() => Math.abs(normalizeAngle(cameraView.value.rotation)) < 0.002);
const pxPerMeterLabel = computed(() => {
  const v = pxPerMeter.value;
  return v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2);
});
const scaleBar = computed(() => {
  const pxm = Math.max(pxPerMeter.value, 0.001);
  const candidates = [0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500];
  const target = 88 / pxm;
  const meters = candidates.reduce((best, item) => (Math.abs(Math.log(item / target)) < Math.abs(Math.log(best / target)) ? item : best));
  return {
    px: Math.round(Math.max(28, Math.min(140, meters * pxm))),
    label: meters >= 1 ? `${meters} m` : `${Math.round(meters * 100)} cm`
  };
});

const TEXT_RES = Math.min(3, Math.max(2, typeof window === 'undefined' ? 2 : window.devicePixelRatio || 1));
const FONT = ['-apple-system', 'SF Pro Text', 'PingFang SC', 'Microsoft YaHei', 'Segoe UI', 'system-ui', 'sans-serif'];

function pal(): Palette {
  return getPalette(themeStore.darkMode);
}

function u() {
  return 1 / camera.scale;
}

function ink(screenPixels: number) {
  return screenPixels / camera.scale;
}

function curPxm() {
  return camera.scale * SCALE;
}

function curTier() {
  return lodTier(curPxm());
}

function scaleBucket() {
  return Math.round(Math.log(camera.scale) / Math.log(1.1));
}

// ───────────────────────────── PixiJS 对象 ─────────────────────────────

let app: Application | null = null;
let world: Container | null = null;
let gridLayer: Graphics | null = null;
let rasterSprite: Sprite | null = null;
let cloudSprite: Sprite | null = null;
let zoneLayer: Graphics | null = null;
let pathLayer: Graphics | null = null;
let nodeLayer: Graphics | null = null;
let trailLayer: Graphics | null = null;
let taskLayer: Graphics | null = null;
let vehicleLayer: Container | null = null;
let overlayLayer: Graphics | null = null;
let screenLayer: Container | null = null;
let rulerGfx: Graphics | null = null;
let tagGfx: Graphics | null = null;
let canvasEl: HTMLCanvasElement | null = null;
let disposed = false;
let cloudKey = '';
let rasterKey = '';

class TextPool {
  private map = new Map<string, Text>();

  private used = new Set<string>();

  constructor(
    private parent: Container,
    private style: () => TextStyleOptions,
    private anchorX: number,
    private anchorY: number
  ) {}

  begin() {
    this.used.clear();
  }

  acquire(key: string, text: string, budget: { n: number }): Text | null {
    let item = this.map.get(key);
    if (!item) {
      if (budget.n <= 0) return null;
      budget.n -= 1;
      item = new Text({ text, style: this.style(), resolution: TEXT_RES });
      item.anchor.set(this.anchorX, this.anchorY);
      item.roundPixels = true;
      this.parent.addChild(item);
      this.map.set(key, item);
    } else if (item.text !== text) {
      item.text = text;
    }
    this.used.add(key);
    item.visible = true;
    return item;
  }

  end() {
    for (const [key, item] of this.map) {
      if (!this.used.has(key)) item.visible = false;
    }
    if (this.map.size > 640) {
      for (const [key, item] of this.map) {
        if (this.map.size <= 420) break;
        if (this.used.has(key)) continue;
        item.destroy();
        this.map.delete(key);
      }
    }
  }

  restyle() {
    for (const item of this.map.values()) item.style = this.style() as never;
  }

  clear() {
    for (const item of this.map.values()) item.destroy();
    this.map.clear();
    this.used.clear();
  }
}

let nodePool: TextPool | null = null;
let zonePool: TextPool | null = null;
let rulerPool: TextPool | null = null;

function labelStyle(kind: 'node' | 'zone' | 'ruler' | 'vehicle'): TextStyleOptions {
  const p = pal();
  if (kind === 'ruler') {
    return { fontFamily: FONT, fontSize: 10, fontWeight: '500', fill: p.gridText, stroke: { color: p.bg, width: 3, join: 'round' } };
  }
  if (kind === 'zone') {
    return { fontFamily: FONT, fontSize: 11, fontWeight: '500', fill: p.textMuted, stroke: { color: p.halo, width: 3, join: 'round' } };
  }
  if (kind === 'vehicle') {
    return { fontFamily: FONT, fontSize: 11, fontWeight: '600', fill: p.text };
  }
  return { fontFamily: FONT, fontSize: 11, fontWeight: '500', fill: p.text, stroke: { color: p.halo, width: 3.4, join: 'round' } };
}

// ───────────────────────────── 图预处理 ─────────────────────────────

interface NodeEntry {
  node: Fms.MapNodeDto;
  x: number;
  y: number;
  kind: StationKind;
}

interface PathGeom {
  id: string;
  ids: string[];
  startId: string;
  endId: string;
  pts: Pt[];
  bounds: Bounds;
  curve: boolean;
  roadWidth: number;
  fwd: boolean;
  bwd: boolean;
  length: number;
}

interface ZoneGeom {
  id: string;
  name: string;
  poly: Pt[];
  bounds: Bounds;
  center: Pt;
  capacity: number;
}

let nodeList: NodeEntry[] = [];
let nodeIndex = new Map<string, NodeEntry>();
let pathGeoms: PathGeom[] = [];
let pathGeomById = new Map<string, PathGeom>();
let zoneGeoms: ZoneGeom[] = [];
let graphBounds: Bounds | null = null;
let staticRev = 0;

function prepareGraph() {
  const graph = props.graph;
  nodeList = [];
  nodeIndex = new Map();
  pathGeoms = [];
  pathGeomById = new Map();
  zoneGeoms = [];
  graphBounds = null;
  staticRev += 1;
  if (!graph) return;

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const node of graph.nodes) {
    const entry: NodeEntry = { node, x: node.x * SCALE, y: -node.y * SCALE, kind: stationKindOf(node.type) };
    nodeList.push(entry);
    nodeIndex.set(node.id, entry);
    if (entry.x < minX) minX = entry.x;
    if (entry.y < minY) minY = entry.y;
    if (entry.x > maxX) maxX = entry.x;
    if (entry.y > maxY) maxY = entry.y;
  }
  if (nodeList.length) graphBounds = { minX, minY, maxX, maxY };

  const merged = new Map<string, PathGeom>();
  for (const path of graph.paths) {
    const a = nodeIndex.get(path.startNodeId);
    const b = nodeIndex.get(path.endNodeId);
    if (!a || !b) continue;
    const curve = path.pathType === 'CubicBezier' && !!path.controlPoints && path.controlPoints.length >= 2;
    const key = `${[path.startNodeId, path.endNodeId].sort().join('|')}${curve ? '|c' : '|s'}`;
    const bidirectional = Boolean(path.isBidirectional || path.bidirectional);
    const backward = path.direction === 'backward';

    let geom = merged.get(key);
    if (!geom) {
      let pts: Pt[];
      if (curve) {
        const steps = clamp(Math.ceil(Math.hypot(b.x - a.x, b.y - a.y) / 14), 10, 30);
        pts = sampleBezier(
          a.x,
          a.y,
          path.controlPoints[0].x * SCALE,
          -path.controlPoints[0].y * SCALE,
          path.controlPoints[1].x * SCALE,
          -path.controlPoints[1].y * SCALE,
          b.x,
          b.y,
          steps
        );
      } else {
        pts = [
          { x: a.x, y: a.y },
          { x: b.x, y: b.y }
        ];
      }
      geom = {
        id: path.id,
        ids: [path.id],
        startId: path.startNodeId,
        endId: path.endNodeId,
        pts,
        bounds: boundsOfPoints(pts),
        curve,
        roadWidth: path.roadWidth && path.roadWidth > 0 ? path.roadWidth : 1.2,
        fwd: false,
        bwd: false,
        length: polylineLength(pts)
      };
      merged.set(key, geom);
      pathGeoms.push(geom);
    } else {
      geom.ids.push(path.id);
    }
    pathGeomById.set(path.id, geom);

    const sameOrientation = geom.startId === path.startNodeId;
    const ownForward = bidirectional || !backward;
    const ownBackward = bidirectional || backward;
    if (sameOrientation) {
      geom.fwd ||= ownForward;
      geom.bwd ||= ownBackward;
    } else {
      geom.fwd ||= ownBackward;
      geom.bwd ||= ownForward;
    }
  }
  for (const geom of pathGeoms) {
    if (!geom.fwd && !geom.bwd) geom.fwd = true;
  }

  for (const zone of graph.mutexZones || []) {
    let poly: Pt[] = [];
    if (zone.polygon && zone.polygon.length >= 3) {
      poly = zone.polygon.map(pt => ({ x: pt.x * SCALE, y: -pt.y * SCALE }));
    } else {
      const members = (zone.memberNodeIds || []).map(id => nodeIndex.get(id)).filter((item): item is NodeEntry => !!item);
      if (members.length) {
        const b = boundsOfPoints(members.map(m => ({ x: m.x, y: m.y })));
        const pad = 1.2 * SCALE;
        poly = [
          { x: b.minX - pad, y: b.minY - pad },
          { x: b.maxX + pad, y: b.minY - pad },
          { x: b.maxX + pad, y: b.maxY + pad },
          { x: b.minX - pad, y: b.maxY + pad }
        ];
      }
    }
    if (poly.length < 3) continue;
    zoneGeoms.push({
      id: zone.zoneId,
      name: zone.name || zone.zoneId,
      poly,
      bounds: boundsOfPoints(poly),
      center: polygonCentroid(poly),
      capacity: zone.maxAllowedVehicles || 1
    });
  }
}

// ───────────────────────────── 初始化与销毁 ─────────────────────────────

let resizeObserver: ResizeObserver | null = null;
let cameraDirty = true;
let userMoved = false;
let labelsStale = true;

onMounted(async () => {
  if (!hostRef.value) return;
  const host = hostRef.value;
  const instance = new Application();
  const palette = pal();
  await instance.init({
    resizeTo: host,
    antialias: true,
    backgroundColor: palette.bg,
    resolution: Math.min(3, Math.max(2, window.devicePixelRatio || 1)),
    autoDensity: true,
    powerPreference: 'high-performance'
  });
  if (disposed) {
    instance.destroy(true, { children: true });
    return;
  }
  app = instance;
  canvasEl = instance.canvas;
  canvasEl.style.touchAction = 'none';
  canvasEl.style.cursor = 'grab';
  host.appendChild(canvasEl);

  world = new Container();
  gridLayer = new Graphics();
  rasterSprite = new Sprite();
  cloudSprite = new Sprite();
  rasterSprite.visible = false;
  cloudSprite.visible = false;
  zoneLayer = new Graphics();
  pathLayer = new Graphics();
  nodeLayer = new Graphics();
  trailLayer = new Graphics();
  taskLayer = new Graphics();
  vehicleLayer = new Container();
  overlayLayer = new Graphics();
  world.addChild(gridLayer, rasterSprite, cloudSprite, zoneLayer, pathLayer, nodeLayer, trailLayer, taskLayer, vehicleLayer, overlayLayer);
  instance.stage.addChild(world);

  screenLayer = new Container();
  rulerGfx = new Graphics();
  const rulerHolder = new Container();
  const zoneHolder = new Container();
  const nodeHolder = new Container();
  tagGfx = new Graphics();
  const vehicleHolder = new Container();
  screenLayer.addChild(rulerGfx, rulerHolder, zoneHolder, nodeHolder, tagGfx, vehicleHolder);
  instance.stage.addChild(screenLayer);
  rulerPool = new TextPool(rulerHolder, () => labelStyle('ruler'), 0, 0.5);
  zonePool = new TextPool(zoneHolder, () => labelStyle('zone'), 0.5, 0.5);
  nodePool = new TextPool(nodeHolder, () => labelStyle('node'), 0, 0.5);
  vehicleTextHolder = vehicleHolder;

  camera.setViewport(host.clientWidth, host.clientHeight);
  prepareGraph();
  if (props.graph) {
    renderRaster();
    fitToScreen(false);
  }
  syncVehicles(props.vehicles || []);

  resizeObserver = new ResizeObserver(() => {
    if (!hostRef.value) return;
    camera.setViewport(hostRef.value.clientWidth, hostRef.value.clientHeight);
    if (!userMoved && props.graph) fitToScreen(false);
    cameraDirty = true;
    labelsStale = true;
  });
  resizeObserver.observe(host);

  bindCanvasEvents();
  instance.ticker.add(onTick);
});

let vehicleTextHolder: Container | null = null;

onBeforeUnmount(() => {
  disposed = true;
  resizeObserver?.disconnect();
  resizeObserver = null;
  unbindCanvasEvents();
  window.clearTimeout(toastTimer);
  nodePool?.clear();
  zonePool?.clear();
  rulerPool?.clear();
  if (app) {
    app.ticker.remove(onTick);
    app.destroy(true, { children: true });
    app = null;
  }
});

// ───────────────────────────── 监听外部数据 ─────────────────────────────

watch(
  () => props.graph,
  () => {
    prepareGraph();
    rasterKey = '';
    cloudKey = '';
    renderRaster();
    if (!userMoved) fitToScreen(false);
    labelsStale = true;
  },
  { deep: true }
);

watch(
  () => props.layerVisibility,
  () => {
    staticRev += 1;
    labelsStale = true;
    renderRaster();
    cloudKey = '';
    renderPointCloud();
  },
  { deep: true }
);

watch(
  () => props.raster?.url,
  () => {
    rasterKey = '';
    renderRaster();
    if (!userMoved) fitToScreen(false);
  }
);

watch(
  () => props.pointCloud,
  () => {
    cloudKey = '';
    renderPointCloud();
  }
);

watch(
  () => props.vehicles,
  next => syncVehicles(next),
  { deep: true }
);

watch(
  () => [props.selectedVehicleId, followId.value] as const,
  () => syncVehicles(props.vehicles || [])
);

watch(
  () => themeStore.darkMode,
  () => {
    if (!app) return;
    app.renderer.background.color = pal().bg;
    nodePool?.restyle();
    zonePool?.restyle();
    rulerPool?.restyle();
    for (const state of vehicleSprites.values()) state.tag.style = labelStyle('vehicle') as never;
    staticRev += 1;
    zoneSig = '';
    cloudKey = '';
    renderPointCloud();
    labelsStale = true;
    cameraDirty = true;
    syncVehicles(props.vehicles || []);
  }
);

// ───────────────────────────── 栅格与点云 ─────────────────────────────

function renderRaster() {
  if (!rasterSprite) return;
  const show = props.layerVisibility.raster !== false && Boolean(props.raster?.url);
  rasterSprite.visible = show;
  if (!show || !props.raster || rasterKey === props.raster.url) return;
  rasterKey = props.raster.url;
  const image = new Image();
  image.onload = () => {
    if (!rasterSprite || !props.raster || props.raster.url !== rasterKey) return;
    const texture = Texture.from(image);
    try {
      texture.source.autoGenerateMipmaps = true;
      texture.source.style.scaleMode = 'linear';
      texture.source.style.mipmapFilter = 'linear';
      texture.source.style.maxAnisotropy = 4;
    } catch {
      // 旧版本渲染后端不支持这些参数时保持默认采样
    }
    rasterSprite.texture = texture;
    rasterSprite.x = props.raster.originX * SCALE;
    rasterSprite.y = -props.raster.topY * SCALE;
    rasterSprite.width = props.raster.widthMeters * SCALE;
    rasterSprite.height = props.raster.heightMeters * SCALE;
  };
  image.src = props.raster.url;
}

function renderPointCloud() {
  if (!cloudSprite) return;
  const points = props.pointCloud || [];
  const show = props.layerVisibility.lidarPoints && points.length > 0;
  cloudSprite.visible = show;
  if (!show) return;

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const point of points) {
    if (point.x < minX) minX = point.x;
    if (point.y < minY) minY = point.y;
    if (point.x > maxX) maxX = point.x;
    if (point.y > maxY) maxY = point.y;
  }
  const spanX = Math.max(0.05, maxX - minX);
  const spanY = Math.max(0.05, maxY - minY);
  const pixelsPerMeter = Math.min(4096 / spanX, 4096 / spanY, Math.max(12, curPxm()));
  const dark = themeStore.darkMode ? 'd' : 'l';
  const key = `${dark}:${points.length}:${pixelsPerMeter.toFixed(1)}:${points[0]?.x}:${points[points.length - 1]?.x}`;
  if (key === cloudKey) return;
  cloudKey = key;
  const width = Math.max(1, Math.ceil(spanX * pixelsPerMeter));
  const height = Math.max(1, Math.ceil(spanY * pixelsPerMeter));
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) return;
  const image = context.createImageData(width, height);
  const [r, g, b, a] = pal().cloud;
  for (const point of points) {
    const px = Math.min(width - 1, Math.max(0, Math.floor((point.x - minX) * pixelsPerMeter)));
    const py = Math.min(height - 1, Math.max(0, Math.floor((maxY - point.y) * pixelsPerMeter)));
    const offset = (py * width + px) * 4;
    image.data[offset] = r;
    image.data[offset + 1] = g;
    image.data[offset + 2] = b;
    image.data[offset + 3] = a;
  }
  context.putImageData(image, 0, 0);
  cloudSprite.texture = Texture.from(canvas);
  cloudSprite.x = minX * SCALE;
  cloudSprite.y = -maxY * SCALE;
  cloudSprite.width = spanX * SCALE;
  cloudSprite.height = spanY * SCALE;
}

// ───────────────────────────── 背景网格与标尺 ─────────────────────────────

function gridSteps(pxm: number) {
  const targetMeters = 110 / Math.max(pxm, 0.001);
  const exp = Math.floor(Math.log10(targetMeters));
  const base = 10 ** exp;
  const f = targetMeters / base;
  const mult = f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10;
  const major = mult * base;
  const div = mult === 2 ? 4 : 5;
  return { major, minor: major / div, div };
}

function formatCoord(value: number, step: number) {
  const decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : 2;
  const text = value.toFixed(decimals);
  return text === '-0' ? '0' : text;
}

function drawGrid() {
  if (!gridLayer || !app) return;
  gridLayer.clear();
  const p = pal();
  const pxm = curPxm();
  const { major, minor, div } = gridSteps(pxm);
  const stepPx = minor * SCALE;
  const vb = camera.viewBounds();
  const minorScreen = minor * pxm;
  const minorAlpha = clamp((minorScreen - 9) / 22, 0, 1);

  const x0 = Math.floor(vb.minX / stepPx);
  const x1 = Math.ceil(vb.maxX / stepPx);
  const y0 = Math.floor(vb.minY / stepPx);
  const y1 = Math.ceil(vb.maxY / stepPx);
  if (x1 - x0 > 600 || y1 - y0 > 600) return;

  if (minorAlpha > 0.02) {
    for (let i = x0; i <= x1; i += 1) {
      if (i % div === 0) continue;
      gridLayer.moveTo(i * stepPx, vb.minY).lineTo(i * stepPx, vb.maxY);
    }
    for (let j = y0; j <= y1; j += 1) {
      if (j % div === 0) continue;
      gridLayer.moveTo(vb.minX, j * stepPx).lineTo(vb.maxX, j * stepPx);
    }
    gridLayer.stroke({ width: ink(1), color: p.gridMinor, alpha: minorAlpha * (p.dark ? 0.9 : 1), cap: 'butt' });
  }
  for (let i = x0; i <= x1; i += 1) {
    if (i % div !== 0) continue;
    gridLayer.moveTo(i * stepPx, vb.minY).lineTo(i * stepPx, vb.maxY);
  }
  for (let j = y0; j <= y1; j += 1) {
    if (j % div !== 0) continue;
    gridLayer.moveTo(vb.minX, j * stepPx).lineTo(vb.maxX, j * stepPx);
  }
  gridLayer.stroke({ width: ink(1), color: p.gridMajor, alpha: 1, cap: 'butt' });

  drawRulers(major, div, stepPx, x0, x1, y0, y1);
}

function drawRulers(major: number, div: number, stepPx: number, x0: number, x1: number, y0: number, y1: number) {
  if (!rulerGfx || !rulerPool || !app) return;
  rulerGfx.clear();
  rulerPool.begin();
  const budget = { n: 24 };
  if (Math.abs(normalizeAngle(camera.rotation)) < 0.002 && showRuler.value) {
    const p = pal();
    const w = camera.width;
    const h = camera.height;
    const leftEdge = 0;
    const topEdge = 0;
    for (let i = x0; i <= x1; i += 1) {
      const sx = camera.localToScreen(i * stepPx, 0).x;
      if (sx < 0 || sx > w) continue;
      const isMajor = i % div === 0;
      rulerGfx.moveTo(sx, topEdge).lineTo(sx, topEdge + (isMajor ? 7 : 3.5));
      if (isMajor && sx > 40 && sx < w - 24) {
        const label = rulerPool.acquire(`x${i}`, formatCoord((i / div) * major, major), budget);
        if (label) label.position.set(Math.round(sx + 3), 13);
      }
    }
    for (let j = y0; j <= y1; j += 1) {
      const sy = camera.localToScreen(0, j * stepPx).y;
      if (sy < 0 || sy > h) continue;
      const isMajor = j % div === 0;
      rulerGfx.moveTo(leftEdge, sy).lineTo(leftEdge + (isMajor ? 7 : 3.5), sy);
      if (isMajor && sy > 36 && sy < h - 20) {
        const label = rulerPool.acquire(`y${j}`, formatCoord((-j / div) * major, major), budget);
        if (label) label.position.set(10, Math.round(sy - 6));
      }
    }
    rulerGfx.stroke({ width: 1, color: p.gridText, alpha: 0.55 });
  }
  rulerPool.end();
}

const showRuler = computed(() => props.showToolbar || props.showSearch || props.showNavigator);

// ───────────────────────────── 静态地图（车道、站点、互斥区） ─────────────────────────────

interface StaticStamp {
  rev: number;
  tier: number;
  bucket: number;
  region: Bounds | null;
  trigger: Bounds | null;
}

let staticStamp: StaticStamp | null = null;
let lastStaticDraw = 0;

function needsStaticRedraw(now: number): boolean {
  const st = staticStamp;
  if (!st || st.rev !== staticRev) return true;
  if (st.tier !== curTier()) return true;
  if (st.region && st.trigger) {
    const vb = camera.viewBounds();
    if (!boundsContain(st.trigger, vb)) return true;
  }
  if (st.bucket !== scaleBucket() && now - lastStaticDraw > 70) return true;
  return false;
}

function dashSegments(g: Graphics, pts: Pt[], dash: number, gap: number, offset = 0) {
  const period = dash + gap;
  let carry = -offset;
  for (let i = 0; i < pts.length - 1; i += 1) {
    const a = pts[i];
    const b = pts[i + 1];
    const len = Math.hypot(b.x - a.x, b.y - a.y);
    if (len < 1e-6) continue;
    const ux = (b.x - a.x) / len;
    const uy = (b.y - a.y) / len;
    let pos = 0;
    while (pos < len) {
      const phase = ((carry % period) + period) % period;
      if (phase < dash) {
        const seg = Math.min(dash - phase, len - pos);
        g.moveTo(a.x + ux * pos, a.y + uy * pos).lineTo(a.x + ux * (pos + seg), a.y + uy * (pos + seg));
        pos += seg;
        carry += seg;
      } else {
        const skip = Math.min(period - phase, len - pos);
        pos += skip;
        carry += skip;
      }
    }
  }
}

function polylinePath(g: Graphics, pts: Pt[]) {
  g.moveTo(pts[0].x, pts[0].y);
  for (let i = 1; i < pts.length; i += 1) g.lineTo(pts[i].x, pts[i].y);
}

function chevronAt(g: Graphics, x: number, y: number, angle: number, size: number) {
  const ca = Math.cos(angle);
  const sa = Math.sin(angle);
  const back = size * 0.9;
  const spread = size * 0.78;
  const tipX = x + ca * size * 0.5;
  const tipY = y + sa * size * 0.5;
  g.moveTo(x - ca * back * 0.5 - sa * spread, y - sa * back * 0.5 + ca * spread);
  g.lineTo(tipX, tipY);
  g.lineTo(x - ca * back * 0.5 + sa * spread, y - sa * back * 0.5 - ca * spread);
}

function drawStatic(now: number) {
  if (!pathLayer || !nodeLayer || !props.graph) return;
  const p = pal();
  const vis = props.layerVisibility;
  const tier = curTier();
  const pxm = curPxm();
  const uu = u();
  const vb = camera.viewBounds();
  const vw = vb.maxX - vb.minX;
  const vh = vb.maxY - vb.minY;

  const region = tier >= 2 ? expandBounds(vb, vw * 0.6, vh * 0.6) : null;
  const trigger = tier >= 2 ? expandBounds(vb, vw * 0.25, vh * 0.25) : null;

  pathLayer.clear();
  nodeLayer.clear();

  const visible: PathGeom[] = [];
  for (const geom of pathGeoms) {
    if (geom.curve ? !vis.bezierPaths : !vis.straightPaths) continue;
    if (region && !boundsIntersect(geom.bounds, region)) continue;
    if (geom.length * camera.scale < 1.2) continue;
    visible.push(geom);
  }

  if (tier <= 0) {
    for (const geom of visible) {
      polylinePath(pathLayer, geom.pts);
    }
    pathLayer.stroke({ width: ink(1), color: p.laneFaint, alpha: p.dark ? 0.8 : 0.72, cap: 'butt', join: 'round' });
  } else if (tier === 1) {
    for (const geom of visible) polylinePath(pathLayer, geom.pts);
    pathLayer.stroke({ width: ink(1.2), color: p.laneInk, alpha: 0.88, cap: 'butt', join: 'round' });
  } else if (tier === 2) {
    for (const geom of visible) polylinePath(pathLayer, geom.pts);
    pathLayer.stroke({ width: ink(3.6), color: p.laneCasing, alpha: 0.9, cap: 'butt', join: 'round' });
    for (const geom of visible) polylinePath(pathLayer, geom.pts);
    pathLayer.stroke({ width: ink(1.5), color: p.laneInk, alpha: 1, cap: 'butt', join: 'round' });
  } else {
    // 路面：按路宽分组画半透明宽线，再画路沿与虚线中心线
    const byWidth = new Map<number, PathGeom[]>();
    for (const geom of visible) {
      const key = Math.round(geom.roadWidth * 20) / 20;
      const list = byWidth.get(key);
      if (list) list.push(geom);
      else byWidth.set(key, [geom]);
    }
    for (const [width, list] of byWidth) {
      const bandPx = width * pxm;
      if (bandPx < 6) continue;
      for (const geom of list) polylinePath(pathLayer, geom.pts);
      pathLayer.stroke({ width: width * SCALE, color: p.laneInk, alpha: p.dark ? 0.07 : 0.055, cap: 'butt', join: 'round' });
    }
    for (const [width, list] of byWidth) {
      if (width * pxm < 9) continue;
      for (const geom of list) {
        const edges = offsetPolyline(geom.pts, (width * SCALE) / 2);
        polylinePath(pathLayer, edges.left);
        polylinePath(pathLayer, edges.right);
      }
    }
    pathLayer.stroke({ width: ink(1), color: p.laneEdge, alpha: 0.95, cap: 'butt', join: 'round' });

    for (const geom of visible) {
      if (geom.length * camera.scale < 12) {
        polylinePath(pathLayer, geom.pts);
      } else {
        dashSegments(pathLayer, geom.pts, ink(7), ink(5), 0);
      }
    }
    pathLayer.stroke({ width: ink(1.3), color: p.laneInk, alpha: 0.82, cap: 'butt', join: 'round' });
  }

  // 方向箭头
  if (vis.arrows && tier >= 2) {
    const size = ink(tier >= 3 ? 6.4 : 5.4);
    for (const geom of visible) {
      const lenPx = geom.length * camera.scale;
      if (lenPx < 30) continue;
      const count = tier >= 3 ? Math.max(1, Math.floor(lenPx / 78)) : 1;
      const distances: number[] = [];
      for (let k = 0; k < count; k += 1) distances.push(((k + 0.5) / count) * geom.length);
      const marks = pointsAtDistances(geom.pts, distances);
      for (const mark of marks) {
        if (geom.fwd && geom.bwd) {
          const nx = Math.cos(mark.angle) * ink(5.5);
          const ny = Math.sin(mark.angle) * ink(5.5);
          chevronAt(pathLayer, mark.x + nx, mark.y + ny, mark.angle, size);
          chevronAt(pathLayer, mark.x - nx, mark.y - ny, mark.angle + Math.PI, size);
        } else {
          chevronAt(pathLayer, mark.x, mark.y, geom.fwd ? mark.angle : mark.angle + Math.PI, size);
        }
      }
    }
    pathLayer.stroke({ width: ink(tier >= 3 ? 1.6 : 1.4), color: p.laneInk, alpha: 0.9, cap: 'round', join: 'round' });
  }

  // 站点
  if (vis.nodes) {
    const specials: NodeEntry[] = [];
    const dotSlots = new Set<number>();
    let dots = 0;
    const slot = ink(5);
    const showDots = tier >= 2;
    for (const entry of nodeList) {
      if (region && (entry.x < region.minX || entry.x > region.maxX || entry.y < region.minY || entry.y > region.maxY)) continue;
      if (entry.kind !== 'landmark') {
        if (tier >= 1) specials.push(entry);
        continue;
      }
      if (!showDots) continue;
      const key = Math.round(entry.x / slot) * 100003 + Math.round(entry.y / slot);
      if (dotSlots.has(key)) continue;
      dotSlots.add(key);
      nodeLayer.circle(entry.x, entry.y, ink(tier >= 3 ? 2.8 : 2.2));
      dots += 1;
    }
    if (dots > 0) {
      nodeLayer.fill({ color: p.dark ? 0x8696a8 : 0x748394, alpha: tier >= 3 ? 1 : 0.85 });
    }
    for (const entry of specials) drawStation(nodeLayer, entry.kind, entry.x, entry.y, uu, p, tier);
  }

  staticStamp = { rev: staticRev, tier, bucket: scaleBucket(), region, trigger };
  lastStaticDraw = now;
  labelsStale = true;
}

// 互斥区：占用状态变化或缩放档位变化时重画
let zoneSig = '';
let zoneBucket = 0;

function zoneSignature() {
  const live = props.mutexZones || [];
  let sig = '';
  for (const z of live) sig += `${z.zoneId}:${z.currentOccupantVehicleIds?.length || 0};`;
  return `${sig}|${props.layerVisibility.mutexZones ? 1 : 0}|${themeStore.darkMode ? 1 : 0}`;
}

function drawZones() {
  if (!zoneLayer) return;
  const sig = zoneSignature();
  const bucket = scaleBucket();
  if (sig === zoneSig && bucket === zoneBucket) return;
  zoneSig = sig;
  zoneBucket = bucket;
  zoneLayer.clear();
  if (!props.layerVisibility.mutexZones) return;
  const p = pal();
  const live = new Map((props.mutexZones || []).map(z => [z.zoneId, z]));
  for (const zone of zoneGeoms) {
    const occupied = (live.get(zone.id)?.currentOccupantVehicleIds?.length || 0) > 0;
    const color = occupied ? p.zoneBusy : p.zoneFree;
    const flat: number[] = [];
    for (const pt of zone.poly) flat.push(pt.x, pt.y);
    zoneLayer.poly(flat);
    zoneLayer.fill({ color, alpha: occupied ? 0.15 : 0.04 });
    if (occupied) {
      zoneLayer.stroke({ width: ink(1.6), color, alpha: 0.9, join: 'round' });
    } else {
      const ring = [...zone.poly, zone.poly[0]];
      dashSegments(zoneLayer, ring, ink(6), ink(4), 0);
      zoneLayer.stroke({ width: ink(1), color, alpha: 0.62, cap: 'butt' });
    }
  }
}

// ───────────────────────────── 车辆 ─────────────────────────────

interface VehicleRender {
  id: string;
  data: Fms.VehicleDto;
  cur: Fms.Pose2D;
  tgt: Fms.Pose2D;
  container: Container;
  laser: Graphics;
  body: Graphics;
  upright: Container;
  ring: Graphics;
  badge: Graphics;
  tag: Text;
  key: string;
  trail: number[];
  lastTx: number;
  lastTy: number;
}

const vehicleSprites = new Map<string, VehicleRender>();

function vehicleLabel(v: Fms.VehicleDto) {
  return vehicleCaption(v.id, props.vehicles);
}

function createVehicle(v: Fms.VehicleDto): VehicleRender {
  const container = new Container();
  const laser = new Graphics();
  const body = new Graphics();
  const upright = new Container();
  const ring = new Graphics();
  const badge = new Graphics();
  upright.addChild(ring, badge);
  container.addChild(laser, body, upright);

  const tag = new Text({ text: '', style: labelStyle('vehicle'), resolution: TEXT_RES });
  tag.anchor.set(0, 0.5);
  tag.roundPixels = true;
  tag.visible = false;
  vehicleTextHolder?.addChild(tag);

  const cur = { x: v.pose.x * SCALE, y: -v.pose.y * SCALE, theta: -v.pose.theta };
  container.position.set(cur.x, cur.y);
  container.rotation = cur.theta;
  return {
    id: v.id,
    data: v,
    cur: { ...cur },
    tgt: { ...cur },
    container,
    laser,
    body,
    upright,
    ring,
    badge,
    tag,
    key: '',
    trail: [],
    lastTx: cur.x,
    lastTy: cur.y
  };
}

function drawContextFor(v: Fms.VehicleDto): VehicleDrawContext {
  return {
    u: u(),
    pxPerMeter: curPxm(),
    tier: curTier(),
    palette: pal(),
    selected: props.selectedVehicleId === v.id || followId.value === v.id,
    hovered: hover.value?.kind === 'vehicle' && hover.value.id === v.id
  };
}

function refreshVehicleVisual(state: VehicleRender, force = false) {
  const v = state.data;
  const ctx = drawContextFor(v);
  const key = [
    v.status,
    v.holdingPayload ? 1 : 0,
    Math.round((v.batteryPercent ?? 0) / 2),
    ctx.selected ? 1 : 0,
    ctx.hovered ? 1 : 0,
    ctx.tier,
    scaleBucket(),
    ctx.palette.dark ? 1 : 0
  ].join('|');
  if (!force && key === state.key) return;
  state.key = key;
  drawVehicleBody(state.body, v, ctx);
  drawBatteryRing(state.ring, v, ctx);
  drawStatusBadge(state.badge, v, ctx, 0);

  state.laser.clear();
  if (ctx.tier >= 2 && (v.status === 'Running' || v.status === 'Avoiding')) {
    const m = vehicleMetrics(ctx.pxPerMeter, ctx.tier);
    state.laser.moveTo(m.length / 2, 0);
    state.laser.arc(m.length / 2, 0, 1.15 * SCALE, -Math.PI / 5, Math.PI / 5);
    state.laser.closePath();
    state.laser.fill({ color: vehicleColor(v.status, ctx.palette.dark), alpha: 0.08 });
  }
  const text = `${vehicleLabel(v)}  ${Math.round(v.batteryPercent ?? 0)}%`;
  if (state.tag.text !== text) state.tag.text = text;
}

function syncVehicles(vehicles: Fms.VehicleDto[]) {
  if (!vehicleLayer) return;
  const ids = new Set(vehicles.map(v => v.id));
  for (const [id, state] of vehicleSprites) {
    if (!ids.has(id)) {
      vehicleLayer.removeChild(state.container);
      state.container.destroy({ children: true });
      state.tag.destroy();
      vehicleSprites.delete(id);
      if (followId.value === id) stopFollow();
    }
  }
  for (const v of vehicles) {
    let state = vehicleSprites.get(v.id);
    if (!state) {
      state = createVehicle(v);
      vehicleSprites.set(v.id, state);
      vehicleLayer.addChild(state.container);
    }
    state.data = v;
    state.tgt = { x: v.pose.x * SCALE, y: -v.pose.y * SCALE, theta: -v.pose.theta };
    refreshVehicleVisual(state);
  }
}

const TRAIL_STEP2 = 6 * 6;
const TRAIL_MAX = 150;

function updateVehicleMotion(deltaFrames: number) {
  const lerp = Math.min(1, deltaFrames * 0.2);
  const rot = camera.rotation;
  for (const state of vehicleSprites.values()) {
    const cur = state.cur;
    const tgt = state.tgt;
    const jump = Math.hypot(tgt.x - cur.x, tgt.y - cur.y);
    if (jump > 400) {
      cur.x = tgt.x;
      cur.y = tgt.y;
      cur.theta = tgt.theta;
      state.trail.length = 0;
    } else {
      cur.x += (tgt.x - cur.x) * lerp;
      cur.y += (tgt.y - cur.y) * lerp;
      cur.theta += shortestAngle(cur.theta, tgt.theta) * lerp;
    }
    state.container.position.set(cur.x, cur.y);
    state.container.rotation = cur.theta;
    state.upright.rotation = -(cur.theta + rot);

    const dx = cur.x - state.lastTx;
    const dy = cur.y - state.lastTy;
    if (dx * dx + dy * dy > TRAIL_STEP2) {
      if (dx * dx + dy * dy > 120 * 120) state.trail.length = 0;
      state.trail.push(cur.x, cur.y);
      if (state.trail.length > TRAIL_MAX * 2) state.trail.splice(0, 2);
      state.lastTx = cur.x;
      state.lastTy = cur.y;
    }
  }
}

// ───────────────────────────── 每帧叠加层：尾迹、任务路径、高亮 ─────────────────────────────

let overlayPhase = 0;

interface Pulse {
  x: number;
  y: number;
  start: number;
  duration: number;
  color: number;
  radius: number;
}

const pulses: Pulse[] = [];

function addPulse(x: number, y: number, radius = 30, duration = 1500, repeat = 2) {
  const now = performance.now();
  for (let k = 0; k < repeat; k += 1) {
    pulses.push({ x, y, start: now + k * duration * 0.55, duration, color: pal().accent, radius });
  }
}

function drawTrails() {
  if (!trailLayer) return;
  trailLayer.clear();
  const targets = new Set<string>();
  if (props.selectedVehicleId) targets.add(props.selectedVehicleId);
  if (followId.value) targets.add(followId.value);
  if (!targets.size || curTier() < 1) return;
  const p = pal();
  for (const id of targets) {
    const state = vehicleSprites.get(id);
    if (!state || state.trail.length < 4) continue;
    const trail = state.trail.slice();
    trail.push(state.cur.x, state.cur.y);
    const count = trail.length / 2;
    const bands = 6;
    const color = vehicleColor(state.data.status, p.dark);
    for (let band = 0; band < bands; band += 1) {
      const from = Math.floor((band / bands) * (count - 1));
      const to = Math.ceil(((band + 1) / bands) * (count - 1));
      if (to <= from) continue;
      trailLayer.moveTo(trail[from * 2], trail[from * 2 + 1]);
      for (let i = from + 1; i <= to; i += 1) trailLayer.lineTo(trail[i * 2], trail[i * 2 + 1]);
      trailLayer.stroke({ width: ink(1.2 + band * 0.3), color, alpha: 0.08 + (band / (bands - 1)) * 0.6, cap: 'round', join: 'round' });
    }
  }
}

function drawTaskOverlay(deltaFrames: number) {
  if (!taskLayer) return;
  taskLayer.clear();
  const orders = props.orders || [];
  if (orders.length === 0) return;

  overlayPhase = (overlayPhase + deltaFrames / 90) % 1;
  const accent = pal().accent;

  for (const order of orders) {
    const route = order.route;
    if (!route || route.length < 2) continue;
    const pts = route.map(([x, y]) => ({ x: x * SCALE, y: -y * SCALE }));
    const sprite = order.assignedVehicleId ? vehicleSprites.get(order.assignedVehicleId) : undefined;
    if (sprite) pts[0] = { x: sprite.cur.x, y: sprite.cur.y };

    const selected = !!props.selectedVehicleId && props.selectedVehicleId === order.assignedVehicleId;
    const width = ink(selected ? 2.4 : 1.6);
    const alpha = selected ? 0.95 : 0.62;
    const dash = ink(selected ? 8 : 6);
    const gap = ink(selected ? 5 : 6);

    dashSegments(taskLayer, pts, dash, gap, overlayPhase * (dash + gap));
    taskLayer.stroke({ width, color: accent, alpha, cap: 'round', join: 'round' });

    const end = pts[pts.length - 1];
    taskLayer.circle(end.x, end.y, ink(selected ? 4 : 3));
    taskLayer.fill({ color: accent, alpha: 0.95 });
    taskLayer.circle(end.x, end.y, ink(5 + overlayPhase * 11));
    taskLayer.stroke({ width: ink(1.2), color: accent, alpha: (1 - overlayPhase) * 0.6 });
  }
}

function resolveConflictPoint(c: Fms.ConflictAlertDto): Pt | null {
  const node = nodeIndex.get(c.conflictLocation);
  if (node) return { x: node.x, y: node.y };
  const zone = zoneGeoms.find(z => z.id === c.conflictLocation);
  if (zone) return zone.center;
  const a = vehicleSprites.get(c.vehicle1Id);
  const b = vehicleSprites.get(c.vehicle2Id);
  if (a && b) return { x: (a.cur.x + b.cur.x) / 2, y: (a.cur.y + b.cur.y) / 2 };
  if (a) return { x: a.cur.x, y: a.cur.y };
  if (b) return { x: b.cur.x, y: b.cur.y };
  return null;
}

function drawHighlights() {
  if (!overlayLayer) return;
  overlayLayer.clear();
  const p = pal();
  const now = performance.now();
  const tier = curTier();

  // 选中路径
  if (props.selectedPathId) {
    const geom = pathGeomById.get(props.selectedPathId);
    if (geom) {
      polylinePath(overlayLayer, geom.pts);
      overlayLayer.stroke({ width: ink(4.4), color: p.select, alpha: 0.22, cap: 'round', join: 'round' });
      polylinePath(overlayLayer, geom.pts);
      overlayLayer.stroke({ width: ink(2), color: p.select, alpha: 1, cap: 'round', join: 'round' });
    }
  }

  // 选中与悬停站点
  const drawNodeRing = (entry: NodeEntry | undefined, color: number, selected: boolean) => {
    if (!entry) return;
    const r = (stationRadius(entry.kind, tier) + (selected ? 5 : 4)) * u();
    overlayLayer!.circle(entry.x, entry.y, r);
    overlayLayer!.stroke({ width: ink(selected ? 1.8 : 1.3), color, alpha: 1 });
    if (selected) {
      overlayLayer!.circle(entry.x, entry.y, r + ink(4));
      overlayLayer!.stroke({ width: ink(1), color, alpha: 0.35 });
    }
  };
  if (props.selectedNodeId) drawNodeRing(nodeIndex.get(props.selectedNodeId), p.select, true);
  if (hover.value?.kind === 'node' && hover.value.id !== props.selectedNodeId) drawNodeRing(nodeIndex.get(hover.value.id), p.hover, false);

  // 冲突位置
  const phase = (now % 1400) / 1400;
  for (const c of props.conflicts || []) {
    const pt = resolveConflictPoint(c);
    if (!pt) continue;
    overlayLayer.circle(pt.x, pt.y, ink(7 + phase * 10));
    overlayLayer.stroke({ width: ink(1.4), color: p.danger, alpha: (1 - phase) * 0.7 });
    overlayLayer.circle(pt.x, pt.y, ink(3.4));
    overlayLayer.fill({ color: p.danger, alpha: 0.95 });
    overlayLayer.stroke({ width: ink(1.2), color: p.halo, alpha: 0.95 });
  }

  // 搜索与聚焦脉冲
  for (let i = pulses.length - 1; i >= 0; i -= 1) {
    const pulse = pulses[i];
    const t = (now - pulse.start) / pulse.duration;
    if (t >= 1) {
      pulses.splice(i, 1);
      continue;
    }
    if (t < 0) continue;
    const eased = 1 - (1 - t) ** 2;
    overlayLayer.circle(pulse.x, pulse.y, ink(6 + pulse.radius * eased));
    overlayLayer.stroke({ width: ink(2 * (1 - t) + 0.6), color: pulse.color, alpha: (1 - t) * 0.85 });
  }
}

// ───────────────────────────── 屏幕空间标注 ─────────────────────────────

class RectIndex {
  private cells = new Map<number, number[]>();

  private cell = 64;

  private forEachCell(x: number, y: number, w: number, h: number, fn: (key: number) => boolean | void) {
    const cx0 = Math.floor(x / this.cell);
    const cx1 = Math.floor((x + w) / this.cell);
    const cy0 = Math.floor(y / this.cell);
    const cy1 = Math.floor((y + h) / this.cell);
    for (let cx = cx0; cx <= cx1; cx += 1) {
      for (let cy = cy0; cy <= cy1; cy += 1) {
        if (fn((cx + 5000) * 10007 + (cy + 5000)) === true) return true;
      }
    }
    return false;
  }

  hit(x: number, y: number, w: number, h: number) {
    return this.forEachCell(x, y, w, h, key => {
      const list = this.cells.get(key);
      if (!list) return false;
      for (let i = 0; i < list.length; i += 4) {
        if (x < list[i] + list[i + 2] && x + w > list[i] && y < list[i + 1] + list[i + 3] && y + h > list[i + 1]) return true;
      }
      return false;
    });
  }

  add(x: number, y: number, w: number, h: number) {
    this.forEachCell(x, y, w, h, key => {
      const list = this.cells.get(key);
      if (list) list.push(x, y, w, h);
      else this.cells.set(key, [x, y, w, h]);
    });
  }
}

function estimateWidth(text: string, fontSize = 11) {
  let w = 0;
  for (let i = 0; i < text.length; i += 1) w += text.charCodeAt(i) > 0x2e80 ? fontSize + 0.5 : fontSize * 0.58;
  return w;
}

function nodeCaptionOf(entry: NodeEntry) {
  const n = entry.node;
  return n.name && n.name !== n.id ? n.name : n.id;
}

function updateNodeLabels() {
  if (!nodePool || !app) return;
  nodePool.begin();
  const vis = props.layerVisibility;
  const tier = curTier();
  const pxm = curPxm();
  const w = camera.width;
  const h = camera.height;
  const budget = { n: 18 };
  const index = new RectIndex();
  const placeables: Array<{ entry: NodeEntry; sx: number; sy: number; priority: number; dist: number }> = [];
  const cx = w / 2;
  const cy = h / 2;
  const selectedId = props.selectedNodeId;
  const hoverId = hover.value?.kind === 'node' ? hover.value.id : null;

  if (vis.nodes) {
    for (const entry of nodeList) {
      const isSelected = entry.node.id === selectedId || entry.node.id === hoverId;
      let eligible = isSelected;
      if (!eligible && vis.stationLabels) {
        eligible = entry.kind === 'landmark' ? pxm >= 24 : pxm >= 12;
      }
      if (!eligible) continue;
      const s = camera.localToScreen(entry.x, entry.y);
      if (s.x < -60 || s.x > w + 60 || s.y < -20 || s.y > h + 20) continue;
      placeables.push({
        entry,
        sx: s.x,
        sy: s.y,
        priority: isSelected ? 0 : entry.kind === 'landmark' ? 2 : 1,
        dist: (s.x - cx) ** 2 + (s.y - cy) ** 2
      });
    }
    placeables.sort((a, b) => a.priority - b.priority || a.dist - b.dist);
  }

  let placed = 0;
  for (const item of placeables) {
    if (placed >= 170) break;
    const caption = nodeCaptionOf(item.entry);
    const width = estimateWidth(caption) + 4;
    const height = 15;
    const radius = stationRadius(item.entry.kind, tier) + 5;
    const x = item.sx + radius;
    const y = item.sy - height / 2;
    const force = item.priority === 0;
    if (!force && index.hit(x, y, width, height)) continue;
    const label = nodePool.acquire(item.entry.node.id, caption, budget);
    if (!label) continue;
    label.position.set(Math.round(x + 1), Math.round(item.sy));
    index.add(x, y, width, height);
    placed += 1;
  }
  nodePool.end();

  // 互斥区名称
  if (zonePool) {
    zonePool.begin();
    if (vis.mutexZones && tier >= 2) {
      const live = new Map((props.mutexZones || []).map(z => [z.zoneId, z]));
      const zoneBudget = { n: 6 };
      for (const zone of zoneGeoms) {
        const wpx = (zone.bounds.maxX - zone.bounds.minX) * camera.scale;
        if (wpx < 90) continue;
        const s = camera.localToScreen(zone.center.x, zone.center.y);
        if (s.x < -80 || s.x > w + 80 || s.y < -20 || s.y > h + 20) continue;
        const count = live.get(zone.id)?.currentOccupantVehicleIds?.length || 0;
        const label = zonePool.acquire(zone.id, count > 0 ? `${zone.name}  ${count}/${zone.capacity}` : zone.name, zoneBudget);
        if (label) label.position.set(Math.round(s.x), Math.round(s.y));
      }
    }
    zonePool.end();
  }
  labelsStale = false;
}

function updateVehicleTags() {
  if (!tagGfx || !app) return;
  tagGfx.clear();
  const p = pal();
  const tier = curTier();
  const pxm = curPxm();
  const w = camera.width;
  const h = camera.height;
  const index = new RectIndex();
  const followed = followId.value;
  const hovered = hover.value?.kind === 'vehicle' ? hover.value.id : null;
  const showAll = tier >= 2 || (tier === 1 && vehicleSprites.size <= 10);

  const order: Array<{ state: VehicleRender; sx: number; sy: number; priority: number; dist: number }> = [];
  for (const state of vehicleSprites.values()) {
    const s = camera.localToScreen(state.cur.x, state.cur.y);
    const special = state.id === props.selectedVehicleId || state.id === followed || state.id === hovered;
    const visibleOnScreen = s.x > -40 && s.x < w + 40 && s.y > -20 && s.y < h + 20;
    state.tag.visible = false;
    if (!visibleOnScreen) continue;
    if (!special && !showAll) continue;
    order.push({ state, sx: s.x, sy: s.y, priority: special ? 0 : 1, dist: (s.x - w / 2) ** 2 + (s.y - h / 2) ** 2 });
  }
  order.sort((a, b) => a.priority - b.priority || a.dist - b.dist);

  for (const item of order) {
    const { state } = item;
    const m = vehicleMetrics(pxm, tier);
    const bodyHalf = (m.length * camera.scale) / 2;
    const offset = Math.max(10, bodyHalf + (tier >= 3 ? 10 : 6));
    const textW = state.tag.width;
    const tagW = textW + 16;
    const tagH = 18;
    const x = item.sx + offset;
    const y = item.sy - tagH / 2;
    const xl = item.sx - offset - tagW;
    let tx = x;
    if (item.priority !== 0 && index.hit(x, y, tagW, tagH)) {
      if (index.hit(xl, y, tagW, tagH)) continue;
      tx = xl;
    }
    if (tx + tagW < 0 || tx > w) continue;
    index.add(tx, y, tagW, tagH);

    const color = vehicleColor(state.data.status, p.dark);
    const selected = state.id === props.selectedVehicleId || state.id === followed;
    tagGfx.roundRect(tx, y, tagW, tagH, 4);
    tagGfx.fill({ color: p.halo, alpha: 0.94 });
    tagGfx.stroke({ width: 1, color: selected ? p.select : p.laneEdge, alpha: selected ? 1 : 0.85 });
    tagGfx.roundRect(tx + 1, y + 3, 3, tagH - 6, 1.5);
    tagGfx.fill({ color, alpha: 1 });
    // 电量小条
    const bw = Math.max(0, Math.min(1, (state.data.batteryPercent ?? 0) / 100)) * (tagW - 14);
    if (tier >= 2) {
      tagGfx.roundRect(tx + 8, y + tagH - 3, tagW - 14, 1.5, 0.75);
      tagGfx.fill({ color: p.laneEdge, alpha: 0.35 });
      tagGfx.roundRect(tx + 8, y + tagH - 3, bw, 1.5, 0.75);
      tagGfx.fill({ color: batteryColor(state.data.batteryPercent ?? 0, p.dark), alpha: 0.95 });
    }
    state.tag.position.set(Math.round(tx + 9), Math.round(item.sy));
    state.tag.visible = true;
  }
}

// ───────────────────────────── 每帧循环 ─────────────────────────────

let lastTickTs = performance.now();

function onTick(ticker: { deltaTime: number; deltaMS: number }) {
  if (!app || !world) return;
  const now = performance.now();
  const dt = Math.min(100, ticker.deltaMS);
  lastTickTs = now;

  stepCamera(dt, now);

  if (cameraDirty) {
    applyCamera();
    cameraDirty = false;
    cameraView.value = camera.state();
    viewQuad.value = camera.viewQuad().flatMap(pt => [pt.x, pt.y]);
    drawGrid();
    labelsStale = true;
  }

  if (needsStaticRedraw(now)) {
    drawStatic(now);
    for (const state of vehicleSprites.values()) refreshVehicleVisual(state);
    if (props.layerVisibility.lidarPoints) renderPointCloud();
  }
  drawZones();

  updateVehicleMotion(ticker.deltaTime);
  drawTrails();
  drawTaskOverlay(ticker.deltaTime);
  drawHighlights();
  processHover();

  // 错误状态徽标需要脉冲
  const phase = (now % 1200) / 1200;
  for (const state of vehicleSprites.values()) {
    const s = state.data.status;
    if (s === 'Error' || s === 'Fault' || s === 'Estop') drawStatusBadge(state.badge, state.data, drawContextFor(state.data), phase);
  }

  if (labelsStale) updateNodeLabels();
  updateVehicleTags();
}

function applyCamera() {
  if (!world) return;
  world.pivot.set(camera.cx, camera.cy);
  world.position.set(camera.width / 2, camera.height / 2);
  world.scale.set(camera.scale);
  world.rotation = camera.rotation;
}

function touchCamera() {
  cameraDirty = true;
}

// ───────────────────────────── 相机运动：飞行、缩放平滑、惯性、键盘、跟随、巡航 ─────────────────────────────

let zoomActive = false;
let zoomTarget = 1;
let zoomAnchor = { x: 0, y: 0 };
let inertia: { vx: number; vy: number } | null = null;
const heldKeys = new Set<string>();

let followSoftUntil = 0;
let followZoomTarget: number | null = null;
let followWaitFlight = false;
let followByCruise = false;

function stepCamera(dt: number, now: number) {
  if (camera.step(dt)) touchCamera();
  const flying = camera.flying;

  // 滚轮缩放的平滑逼近
  if (zoomActive && !flying) {
    const k = 1 - Math.exp(-dt / 80);
    const ls = Math.log(camera.scale);
    const lt = Math.log(zoomTarget);
    let next = Math.exp(ls + (lt - ls) * k);
    if (Math.abs(lt - ls) < 0.0015) {
      next = zoomTarget;
      zoomActive = false;
    }
    const anchor = followId.value ? { x: camera.width / 2, y: camera.height / 2 } : zoomAnchor;
    camera.zoomAt(anchor.x, anchor.y, next);
    touchCamera();
  }

  // 拖拽惯性
  if (inertia && !flying) {
    camera.panBy(inertia.vx * dt, inertia.vy * dt);
    const decay = Math.exp(-dt / 240);
    inertia.vx *= decay;
    inertia.vy *= decay;
    if (Math.hypot(inertia.vx, inertia.vy) < 0.02) inertia = null;
    touchCamera();
  }

  // 键盘平移
  if (heldKeys.size && !flying) {
    const speed = (heldKeys.has('Shift') ? 1500 : 560) * (dt / 1000);
    let dx = 0;
    let dy = 0;
    if (heldKeys.has('ArrowLeft')) dx += speed;
    if (heldKeys.has('ArrowRight')) dx -= speed;
    if (heldKeys.has('ArrowUp')) dy += speed;
    if (heldKeys.has('ArrowDown')) dy -= speed;
    if (dx || dy) {
      stopFollow();
      userMoved = true;
      camera.panBy(dx, dy);
      touchCamera();
    }
  }

  // 跟随
  if (followId.value) {
    const state = vehicleSprites.get(followId.value);
    if (!state) {
      stopFollow();
    } else if (followWaitFlight) {
      if (!flying) followWaitFlight = false;
    } else if (!flying) {
      const k = 1 - Math.exp(-dt / 150);
      camera.cx += (state.cur.x - camera.cx) * k;
      camera.cy += (state.cur.y - camera.cy) * k;
      if (followZoomTarget !== null) {
        const ls = Math.log(camera.scale);
        const lt = Math.log(followZoomTarget);
        camera.scale = clamp(Math.exp(ls + (lt - ls) * k), MIN_ZOOM, MAX_ZOOM);
        if (Math.abs(lt - ls) < 0.01) followZoomTarget = null;
      }
      if (followHeading.value) {
        const goal = -Math.PI / 2 - state.cur.theta;
        camera.rotation += shortestAngle(camera.rotation, goal) * k;
      }
      touchCamera();
      if (followSoftUntil && now > followSoftUntil) stopFollow();
    }
  }

  stepCruise(dt, now);
}

function startFlight(goal: CameraState, options?: { duration?: number; speed?: number }, keepFollow = false) {
  zoomActive = false;
  inertia = null;
  if (!keepFollow) stopFollow();
  userMoved = true;
  camera.flyTo(goal, options);
  touchCamera();
}

function flyTo(target: FlyTarget, options?: { duration?: number; speed?: number }) {
  const cur = camera.state();
  startFlight(
    {
      cx: target.x !== undefined ? target.x * SCALE : cur.cx,
      cy: target.y !== undefined ? -target.y * SCALE : cur.cy,
      scale: target.scale !== undefined ? target.scale / SCALE : cur.scale,
      rotation: target.rotation !== undefined ? (target.rotation * Math.PI) / 180 : cur.rotation
    },
    options
  );
}

function flyToLocal(x: number, y: number, pxm?: number, keepRotation = true) {
  startFlight({
    cx: x,
    cy: y,
    scale: pxm !== undefined ? pxm / SCALE : camera.scale,
    rotation: keepRotation ? camera.rotation : 0
  });
}

function fitFlat(flat: number[], options: { padding?: number; maxPxm?: number; animated?: boolean } = {}) {
  const target = camera.fitPoints(flat, {
    padding: options.padding ?? 64,
    maxScale: (options.maxPxm ?? 34) / SCALE,
    insets: { top: props.showToolbar || props.showSearch ? 24 : 0 }
  });
  if (!target) return false;
  if (options.animated === false) {
    camera.cancelFlight();
    camera.set(target);
    touchCamera();
  } else {
    startFlight(target);
  }
  return true;
}

function graphFitPoints(): number[] {
  const flat: number[] = [];
  if (!graphBounds) return flat;
  const b = { ...graphBounds };
  const include = (x: number, y: number) => {
    b.minX = Math.min(b.minX, x);
    b.minY = Math.min(b.minY, y);
    b.maxX = Math.max(b.maxX, x);
    b.maxY = Math.max(b.maxY, y);
  };
  const spanX = b.maxX - b.minX;
  const spanY = b.maxY - b.minY;
  const raster = props.raster;
  if (raster && raster.widthMeters > 0 && raster.heightMeters > 0) {
    const rasterW = raster.widthMeters * SCALE;
    const rasterH = raster.heightMeters * SCALE;
    if (rasterW < spanX * 1.35 && rasterH < spanY * 1.35) {
      const rx = raster.originX * SCALE;
      const ry = -raster.topY * SCALE;
      include(rx, ry);
      include(rx + rasterW, ry + rasterH);
    }
  }
  flat.push(b.minX, b.minY, b.maxX, b.minY, b.maxX, b.maxY, b.minX, b.maxY);
  return flat;
}

function fitToScreen(animated = true) {
  if (!app || !props.graph || !graphBounds) return;
  if (camera.width < 32 || camera.height < 32) return;
  const flat = graphFitPoints();
  const rotation = camera.rotation;
  const target = camera.fitPoints(flat, { padding: 40, maxScale: 2.5, rotation, insets: { top: props.showToolbar || props.showSearch ? 20 : 0 } });
  if (!target) return;
  fittedScale.value = target.scale;
  if (animated) startFlight(target);
  else {
    camera.cancelFlight();
    camera.set(target);
    touchCamera();
  }
  userMoved = false;
  activePreset.value = animated ? 'global' : null;
}

function resetView() {
  if (!graphBounds) return;
  const cx = (graphBounds.minX + graphBounds.maxX) / 2;
  const cy = (graphBounds.minY + graphBounds.maxY) / 2;
  startFlight({ cx, cy, scale: 1, rotation: 0 });
  userMoved = true;
}

function zoomBy(factor: number) {
  zoomActive = false;
  inertia = null;
  userMoved = true;
  const cur = camera.scale;
  const next = clamp(cur * factor, MIN_ZOOM, MAX_ZOOM);
  const target = camera.state();
  target.scale = next;
  startFlight(target, { duration: 260 }, !!followId.value);
}

function zoomIn() {
  zoomBy(1.4);
}

function zoomOut() {
  zoomBy(1 / 1.4);
}

function rotateBy(degrees: number) {
  const target = camera.state();
  target.rotation = camera.rotation + (degrees * Math.PI) / 180;
  startFlight(target, { duration: 380 }, !!followId.value);
}

function resetRotation() {
  const target = camera.state();
  target.rotation = 0;
  followHeading.value = false;
  startFlight(target, { duration: 420 }, !!followId.value);
}

function focusPoint(x: number, y: number, scale?: number) {
  flyTo({ x, y, scale: scale ?? Math.max(curPxm(), FOCUS_PX_PER_M) });
}

function focusNode(id: string, pulse = true) {
  const entry = nodeIndex.get(id);
  if (!entry) return false;
  flyToLocal(entry.x, entry.y, Math.max(curPxm(), FOCUS_PX_PER_M));
  if (pulse) addPulse(entry.x, entry.y);
  return true;
}

function focusVehicle(id: string, pulse = false) {
  const state = vehicleSprites.get(id);
  if (!state) return false;
  const pxm = Math.max(curPxm(), FOCUS_PX_PER_M);
  const dist = Math.hypot(state.cur.x - camera.cx, state.cur.y - camera.cy) * camera.scale;
  if (followId.value === id) return true;
  stopFollow();
  startFlight({ cx: state.cur.x, cy: state.cur.y, scale: pxm / SCALE, rotation: camera.rotation }, dist > camera.width * 2.5 ? undefined : { duration: 520 });
  // 飞行结束后短暂吸附，补偿车辆在飞行期间的位移
  followId.value = id;
  followWaitFlight = camera.flying;
  followSoftUntil = performance.now() + (camera.flying ? 2400 : 900);
  followZoomTarget = null;
  if (pulse) addPulse(state.cur.x, state.cur.y, 26);
  return true;
}

function followVehicle(id: string | null, options: { zoom?: number; fromCruise?: boolean } = {}) {
  if (!id) {
    stopFollow();
    return;
  }
  const state = vehicleSprites.get(id);
  if (!state) return;
  const wasFollowing = followId.value;
  followId.value = id;
  followSoftUntil = 0;
  followByCruise = !!options.fromCruise;
  userMoved = true;
  zoomActive = false;
  inertia = null;
  const pxm = Math.max(curPxm(), options.zoom ?? 22);
  const dist = Math.hypot(state.cur.x - camera.cx, state.cur.y - camera.cy) * camera.scale;
  if (dist > camera.width * 1.2) {
    camera.flyTo({ cx: state.cur.x, cy: state.cur.y, scale: pxm / SCALE, rotation: camera.rotation });
    followWaitFlight = true;
    followZoomTarget = null;
  } else {
    followWaitFlight = false;
    followZoomTarget = Math.abs(Math.log((pxm / SCALE) / camera.scale)) > 0.02 ? pxm / SCALE : null;
  }
  touchCamera();
  if (wasFollowing !== id) emit('follow-change', id);
  syncVehicles(props.vehicles || []);
}

function stopFollow() {
  if (!followId.value) return;
  const wasSoft = followSoftUntil > 0;
  followId.value = null;
  followWaitFlight = false;
  followZoomTarget = null;
  followSoftUntil = 0;
  followByCruise = false;
  followHeading.value = false;
  if (!wasSoft) emit('follow-change', null);
  syncVehicles(props.vehicles || []);
}

// 任务、互斥区、冲突聚焦

function orderRouteFlat(order: Fms.OrderDto): number[] {
  const flat: number[] = [];
  for (const point of order.route || []) flat.push(point[0] * SCALE, -point[1] * SCALE);
  const state = order.assignedVehicleId ? vehicleSprites.get(order.assignedVehicleId) : undefined;
  if (state) flat.push(state.cur.x, state.cur.y);
  const target = order.targetNodeId ? nodeIndex.get(order.targetNodeId) : undefined;
  if (target) flat.push(target.x, target.y);
  const source = order.sourceNodeId ? nodeIndex.get(order.sourceNodeId) : undefined;
  if (source) flat.push(source.x, source.y);
  return flat;
}

function focusOrder(orderOrId: string | Fms.OrderDto) {
  const order =
    typeof orderOrId === 'string'
      ? (props.orders || []).find(o => o.orderId === orderOrId || o.serial === orderOrId)
      : orderOrId;
  if (!order) return false;
  const flat = orderRouteFlat(order);
  if (flat.length < 2) {
    if (order.assignedVehicleId) return focusVehicle(order.assignedVehicleId, true);
    return false;
  }
  if (flat.length === 2) {
    flyToLocal(flat[0], flat[1], Math.max(curPxm(), FOCUS_PX_PER_M));
    return true;
  }
  return fitFlat(flat, { padding: 96, maxPxm: 30 });
}

function focusZone(id: string) {
  const zone = zoneGeoms.find(z => z.id === id);
  if (!zone) return false;
  const flat: number[] = [];
  for (const pt of zone.poly) flat.push(pt.x, pt.y);
  return fitFlat(flat, { padding: 110, maxPxm: 26 });
}

function focusConflict(conflict: Fms.ConflictAlertDto) {
  const pt = resolveConflictPoint(conflict);
  if (!pt) return false;
  flyToLocal(pt.x, pt.y, Math.max(curPxm(), 22));
  addPulse(pt.x, pt.y, 36);
  return true;
}

function alertPoints(): number[] {
  const flat: number[] = [];
  for (const state of vehicleSprites.values()) {
    const s = state.data.status;
    if (s === 'Error' || s === 'Fault' || s === 'Estop' || s === 'WaitingLock' || s === 'Avoiding') flat.push(state.cur.x, state.cur.y);
  }
  for (const c of props.conflicts || []) {
    const pt = resolveConflictPoint(c);
    if (pt) flat.push(pt.x, pt.y);
  }
  return flat;
}

function applyPreset(preset: ViewPreset) {
  activePreset.value = preset;
  if (preset === 'global') {
    stopFollow();
    fitToScreen(true);
    activePreset.value = 'global';
    return;
  }
  if (preset === 'follow') {
    const running = [...vehicleSprites.values()].find(s => s.data.currentOrderId || s.data.status === 'Running');
    const id = props.selectedVehicleId || running?.id || [...vehicleSprites.keys()][0] || null;
    if (!id) {
      showToast('当前没有可跟随的车辆');
      activePreset.value = null;
      return;
    }
    emit('select-vehicle', id);
    followVehicle(id);
    return;
  }
  if (preset === 'task') {
    let flat: number[] = [];
    const selectedOrder = props.selectedVehicleId
      ? (props.orders || []).find(o => o.assignedVehicleId === props.selectedVehicleId)
      : undefined;
    if (selectedOrder) flat = orderRouteFlat(selectedOrder);
    if (flat.length < 4) for (const order of props.orders || []) flat.push(...orderRouteFlat(order));
    if (flat.length < 2) {
      showToast('当前没有在途任务路径');
      activePreset.value = null;
      return;
    }
    stopFollow();
    if (flat.length === 2) flyToLocal(flat[0], flat[1], Math.max(curPxm(), FOCUS_PX_PER_M));
    else fitFlat(flat, { padding: 100, maxPxm: 30 });
    return;
  }
  const flat = alertPoints();
  if (flat.length < 2) {
    showToast('当前没有异常区域');
    activePreset.value = null;
    return;
  }
  stopFollow();
  if (flat.length === 2) flyToLocal(flat[0], flat[1], Math.max(curPxm(), 22));
  else fitFlat(flat, { padding: 110, maxPxm: 24 });
}

// ───────────────────────────── 巡航 ─────────────────────────────

const CRUISE_DWELL_MS = 7000;
const CRUISE_RESUME_MS = 15000;

const cruiseOn = ref(props.cruise);
const cruisePauseLeft = ref(0);
let cruiseDwell = 0;
let cruiseIndex = -1;
let cruiseStarted = false;

watch(
  () => props.cruise,
  value => {
    if (value !== cruiseOn.value) setCruise(value, false);
  }
);

function setCruise(on: boolean, notify = true) {
  cruiseOn.value = on;
  cruiseStarted = false;
  cruiseDwell = CRUISE_DWELL_MS;
  cruisePauseLeft.value = 0;
  if (!on && followByCruise) stopFollow();
  if (notify) emit('update:cruise', on);
}

function cruiseTargets() {
  const list: Array<{ kind: 'vehicle'; id: string } | { kind: 'conflict'; index: number } | { kind: 'zone'; id: string }> = [];
  const active = [...vehicleSprites.values()]
    .filter(s => s.data.currentOrderId || ['Running', 'Avoiding', 'WaitingLock', 'Error', 'Fault', 'Estop'].includes(s.data.status))
    .sort((a, b) => a.id.localeCompare(b.id, 'zh-CN', { numeric: true }));
  for (const s of active) list.push({ kind: 'vehicle', id: s.id });
  (props.conflicts || []).forEach((_, index) => list.push({ kind: 'conflict', index }));
  if (!list.length) {
    const live = new Map((props.mutexZones || []).map(z => [z.zoneId, z]));
    for (const zone of zoneGeoms) if ((live.get(zone.id)?.currentOccupantVehicleIds?.length || 0) > 0) list.push({ kind: 'zone', id: zone.id });
  }
  return list;
}

function cruiseNext() {
  const targets = cruiseTargets();
  if (!targets.length) {
    stopFollow();
    fitToScreen(true);
    return;
  }
  cruiseIndex = (cruiseIndex + 1) % targets.length;
  const target = targets[cruiseIndex];
  if (target.kind === 'vehicle') {
    emit('select-vehicle', target.id);
    followVehicle(target.id, { zoom: 20, fromCruise: true });
  } else if (target.kind === 'conflict') {
    stopFollow();
    const conflict = (props.conflicts || [])[target.index];
    if (conflict) focusConflict(conflict);
  } else {
    stopFollow();
    focusZone(target.id);
  }
}

function stepCruise(dt: number, now: number) {
  if (!cruiseOn.value) return;
  if (cruisePauseLeft.value > 0) {
    cruisePauseLeft.value = Math.max(0, cruisePauseLeft.value - dt);
    if (cruisePauseLeft.value === 0) cruiseDwell = CRUISE_DWELL_MS;
    return;
  }
  cruiseDwell += dt;
  if (!cruiseStarted || cruiseDwell >= CRUISE_DWELL_MS) {
    cruiseStarted = true;
    cruiseDwell = 0;
    cruiseNext();
  }
  void now;
}

function markInteraction() {
  if (!cruiseOn.value) return;
  cruisePauseLeft.value = CRUISE_RESUME_MS;
  if (followByCruise) stopFollow();
}

const cruiseChip = computed(() => {
  if (!cruiseOn.value) return '';
  if (cruisePauseLeft.value > 0) return `巡航已暂停，${Math.ceil(cruisePauseLeft.value / 1000)} 秒后恢复`;
  return '巡航中';
});

// ───────────────────────────── 书签 ─────────────────────────────

const bookmarks = ref<MapBookmark[]>([]);
const bookmarkOpen = ref(false);

watch(
  () => props.graph?.name,
  name => {
    bookmarks.value = loadBookmarks(name || '');
  },
  { immediate: true }
);

function saveBookmark() {
  if (bookmarks.value.length >= BOOKMARK_LIMIT) {
    showToast(`最多保存 ${BOOKMARK_LIMIT} 个视角`);
    return;
  }
  const mark: MapBookmark = {
    id: makeBookmarkId(),
    name: `视角 ${bookmarks.value.length + 1}`,
    x: camera.cx / SCALE,
    y: -camera.cy / SCALE,
    scale: curPxm(),
    rotation: (normalizeAngle(camera.rotation) * 180) / Math.PI,
    createdAt: Date.now()
  };
  bookmarks.value = [...bookmarks.value, mark];
  saveBookmarks(props.graph?.name || '', bookmarks.value);
  showToast(`已保存「${mark.name}」`);
}

function gotoBookmark(mark: MapBookmark) {
  bookmarkOpen.value = false;
  activePreset.value = null;
  flyTo({ x: mark.x, y: mark.y, scale: mark.scale, rotation: mark.rotation });
}

function removeBookmark(id: string) {
  bookmarks.value = bookmarks.value.filter(item => item.id !== id);
  saveBookmarks(props.graph?.name || '', bookmarks.value);
}

// ───────────────────────────── 命中检测、指针与滚轮 ─────────────────────────────

let hoverRequest: { x: number; y: number } | null = null;

function nodeInteractive(entry: NodeEntry, tier: number) {
  if (!props.layerVisibility.nodes) return false;
  return entry.kind === 'landmark' ? tier >= 2 : tier >= 1;
}

function pickAt(sx: number, sy: number, vehiclesAndNodesOnly = false): PickResult {
  const tier = curTier();
  const pxm = curPxm();

  let bestVehicle: { id: string; d2: number } | null = null;
  const m = vehicleMetrics(pxm, tier);
  const radius = Math.max(11, (m.length * camera.scale) / 2 + 4);
  for (const state of vehicleSprites.values()) {
    const s = camera.localToScreen(state.cur.x, state.cur.y);
    const d2 = (s.x - sx) ** 2 + (s.y - sy) ** 2;
    if (d2 <= radius * radius && (!bestVehicle || d2 < bestVehicle.d2)) bestVehicle = { id: state.id, d2 };
  }
  if (bestVehicle) return { kind: 'vehicle', id: bestVehicle.id };

  let bestNode: { id: string; d2: number } | null = null;
  for (const entry of nodeList) {
    if (!nodeInteractive(entry, tier)) continue;
    const s = camera.localToScreen(entry.x, entry.y);
    const d2 = (s.x - sx) ** 2 + (s.y - sy) ** 2;
    const r = entry.kind === 'landmark' ? 8 : 11;
    if (d2 <= r * r && (!bestNode || d2 < bestNode.d2)) bestNode = { id: entry.node.id, d2 };
  }
  if (bestNode) return { kind: 'node', id: bestNode.id };

  if (vehiclesAndNodesOnly || tier < 1) return null;
  const local = camera.screenToLocal(sx, sy);
  const tol = 7 / camera.scale;
  let bestPath: { id: string; d2: number } | null = null;
  for (const geom of pathGeoms) {
    const b = geom.bounds;
    if (local.x < b.minX - tol || local.x > b.maxX + tol || local.y < b.minY - tol || local.y > b.maxY + tol) continue;
    for (let i = 0; i < geom.pts.length - 1; i += 1) {
      const d2 = distToSegmentSq(local.x, local.y, geom.pts[i].x, geom.pts[i].y, geom.pts[i + 1].x, geom.pts[i + 1].y);
      if (d2 <= tol * tol && (!bestPath || d2 < bestPath.d2)) bestPath = { id: geom.id, d2 };
    }
  }
  return bestPath ? { kind: 'path', id: bestPath.id } : null;
}

function processHover() {
  if (!hoverRequest || pointer) return;
  const { x, y } = hoverRequest;
  hoverRequest = null;
  const hit = pickAt(x, y, true);
  const next: HoverState | null = hit && (hit.kind === 'vehicle' || hit.kind === 'node') ? { kind: hit.kind, id: hit.id, sx: x, sy: y } : null;
  const prev = hover.value;
  if (next && prev && prev.kind === next.kind && prev.id === next.id) {
    if (Math.abs(prev.sx - x) > 2 || Math.abs(prev.sy - y) > 2) hover.value = next;
  } else if (next || prev) {
    hover.value = next;
    labelsStale = true;
    syncVehicles(props.vehicles || []);
  }
  if (canvasEl) canvasEl.style.cursor = next ? 'pointer' : 'grab';
}

interface PointerState {
  id: number;
  mode: 'pan' | 'rotate';
  startX: number;
  startY: number;
  lastX: number;
  lastY: number;
  lastT: number;
  moved: boolean;
  vx: number;
  vy: number;
  lastAngle: number;
}

let pointer: PointerState | null = null;
let boundHandlers: Array<[EventTarget, string, EventListener, AddEventListenerOptions | boolean | undefined]> = [];

function bind(target: EventTarget, type: string, handler: (e: never) => void, options?: AddEventListenerOptions | boolean) {
  target.addEventListener(type, handler as EventListener, options);
  boundHandlers.push([target, type, handler as EventListener, options]);
}

function localPoint(e: MouseEvent) {
  const rect = canvasEl!.getBoundingClientRect();
  return { x: e.clientX - rect.left, y: e.clientY - rect.top };
}

function bindCanvasEvents() {
  if (!canvasEl) return;
  bind(canvasEl, 'pointerdown', onPointerDown as never);
  bind(canvasEl, 'pointermove', onPointerMove as never);
  bind(canvasEl, 'pointerup', onPointerUp as never);
  bind(canvasEl, 'pointercancel', onPointerUp as never);
  bind(canvasEl, 'pointerleave', onPointerLeave as never);
  bind(canvasEl, 'wheel', onWheel as never, { passive: false });
  bind(canvasEl, 'dblclick', onDoubleClick as never);
  bind(canvasEl, 'contextmenu', onContextMenu as never);
  bind(window, 'keydown', onKeyDown as never);
  bind(window, 'keyup', onKeyUp as never);
  bind(window, 'blur', (() => heldKeys.clear()) as never);
}

function unbindCanvasEvents() {
  for (const [target, type, handler, options] of boundHandlers) target.removeEventListener(type, handler, options);
  boundHandlers = [];
}

function onPointerDown(e: PointerEvent) {
  if (e.button === 2) return;
  rootRef.value?.focus({ preventScroll: true });
  closeMenu();
  bookmarkOpen.value = false;
  markInteraction();
  camera.cancelFlight();
  inertia = null;
  zoomActive = false;
  const pt = localPoint(e);
  const rotate = e.shiftKey && e.button === 0;
  pointer = {
    id: e.pointerId,
    mode: rotate ? 'rotate' : 'pan',
    startX: pt.x,
    startY: pt.y,
    lastX: pt.x,
    lastY: pt.y,
    lastT: performance.now(),
    moved: false,
    vx: 0,
    vy: 0,
    lastAngle: Math.atan2(pt.y - camera.height / 2, pt.x - camera.width / 2)
  };
  canvasEl?.setPointerCapture(e.pointerId);
  if (canvasEl) canvasEl.style.cursor = rotate ? 'alias' : 'grabbing';
  hover.value = null;
}

function onPointerMove(e: PointerEvent) {
  const pt = localPoint(e);
  const local = camera.screenToLocal(pt.x, pt.y);
  cursorWorld.value = { x: local.x / SCALE, y: -local.y / SCALE };

  if (!pointer || pointer.id !== e.pointerId) {
    hoverRequest = { x: pt.x, y: pt.y };
    return;
  }
  const dx = pt.x - pointer.lastX;
  const dy = pt.y - pointer.lastY;
  const now = performance.now();
  if (!pointer.moved && Math.hypot(pt.x - pointer.startX, pt.y - pointer.startY) > 3) {
    pointer.moved = true;
    userMoved = true;
    if (pointer.mode === 'pan') stopFollow();
    activePreset.value = null;
  }
  if (!pointer.moved) return;

  if (pointer.mode === 'rotate') {
    const angle = Math.atan2(pt.y - camera.height / 2, pt.x - camera.width / 2);
    camera.rotation += shortestAngle(pointer.lastAngle, angle);
    pointer.lastAngle = angle;
    touchCamera();
  } else {
    camera.panBy(dx, dy);
    const dtMs = Math.max(1, now - pointer.lastT);
    pointer.vx = pointer.vx * 0.6 + (dx / dtMs) * 0.4;
    pointer.vy = pointer.vy * 0.6 + (dy / dtMs) * 0.4;
    touchCamera();
  }
  pointer.lastX = pt.x;
  pointer.lastY = pt.y;
  pointer.lastT = now;
}

function onPointerUp(e: PointerEvent) {
  const state = pointer;
  if (!state || state.id !== e.pointerId) return;
  pointer = null;
  try {
    canvasEl?.releasePointerCapture(e.pointerId);
  } catch {
    // 指针已释放
  }
  if (canvasEl) canvasEl.style.cursor = 'grab';

  if (!state.moved) {
    if (e.type !== 'pointercancel' && e.button === 0) handleClick(state.startX, state.startY);
    return;
  }
  if (state.mode === 'rotate') {
    if (Math.abs(normalizeAngle(camera.rotation)) < (3 * Math.PI) / 180) {
      const target = camera.state();
      target.rotation = 0;
      startFlight(target, { duration: 220 });
    }
    return;
  }
  const speed = Math.hypot(state.vx, state.vy);
  if (speed > 0.18 && performance.now() - state.lastT < 80) {
    inertia = { vx: state.vx, vy: state.vy };
  }
}

function onPointerLeave() {
  hoverRequest = null;
  if (hover.value) {
    hover.value = null;
    labelsStale = true;
    syncVehicles(props.vehicles || []);
  }
}

function handleClick(sx: number, sy: number) {
  const hit = pickAt(sx, sy);
  if (!hit) {
    if (props.deselectable) {
      emit('select-vehicle', null);
      emit('select-node', null);
      emit('select-path', null);
    }
    return;
  }
  if (hit.kind === 'vehicle') emit('select-vehicle', hit.id);
  else if (hit.kind === 'node') emit('select-node', hit.id);
  else emit('select-path', hit.id);
}

function onWheel(e: WheelEvent) {
  e.preventDefault();
  markInteraction();
  camera.cancelFlight();
  inertia = null;
  closeMenu();
  const pt = localPoint(e);
  let delta = e.deltaY;
  if (e.deltaMode === 1) delta *= 33;
  else if (e.deltaMode === 2) delta *= 120;
  delta = clamp(delta, -260, 260);
  const factor = Math.exp(-delta * (e.ctrlKey ? 0.012 : 0.0017));
  const base = zoomActive ? zoomTarget : camera.scale;
  zoomTarget = clamp(base * factor, MIN_ZOOM, MAX_ZOOM);
  zoomAnchor = { x: pt.x, y: pt.y };
  zoomActive = true;
  userMoved = true;
  activePreset.value = null;
}

function onDoubleClick(e: MouseEvent) {
  const pt = localPoint(e);
  markInteraction();
  const hit = pickAt(pt.x, pt.y);
  if (hit?.kind === 'vehicle') {
    emit('select-vehicle', hit.id);
    focusVehicle(hit.id, true);
    return;
  }
  if (hit?.kind === 'node') {
    emit('select-node', hit.id);
    focusNode(hit.id);
    return;
  }
  const local = camera.screenToLocal(pt.x, pt.y);
  startFlight({ cx: local.x, cy: local.y, scale: clamp(camera.scale * 2.2, MIN_ZOOM, MAX_ZOOM), rotation: camera.rotation }, { duration: 420 });
}

// ───────────────────────────── 右键菜单 ─────────────────────────────

const menu = reactive({
  visible: false,
  x: 0,
  y: 0,
  title: '',
  subtitle: '',
  items: [] as MapMenuItem[]
});

let menuContext: {
  pick: PickResult;
  world: { x: number; y: number };
  nearestNode: string | null;
  zoneId: string | null;
} | null = null;

const rootSize = ref({ w: 0, h: 0 });

function closeMenu() {
  menu.visible = false;
  menuContext = null;
}

function nearestNodeTo(localX: number, localY: number, maxDistPx = Infinity) {
  let best: { id: string; d2: number } | null = null;
  for (const entry of nodeList) {
    const d2 = (entry.x - localX) ** 2 + (entry.y - localY) ** 2;
    if (!best || d2 < best.d2) best = { id: entry.node.id, d2 };
  }
  if (!best) return null;
  return Math.sqrt(best.d2) <= maxDistPx ? best : null;
}

function onContextMenu(e: MouseEvent) {
  e.preventDefault();
  markInteraction();
  const pt = localPoint(e);
  const hit = pickAt(pt.x, pt.y);
  const local = camera.screenToLocal(pt.x, pt.y);
  const world = { x: local.x / SCALE, y: -local.y / SCALE };
  const nearest = nearestNodeTo(local.x, local.y);
  const zone = zoneGeoms.find(z => boundsIntersect(z.bounds, { minX: local.x, minY: local.y, maxX: local.x, maxY: local.y }) && pointInPolygon(local.x, local.y, z.poly));
  menuContext = { pick: hit, world, nearestNode: nearest?.id ?? null, zoneId: zone?.id ?? null };

  const items: MapMenuItem[] = [];
  if (hit?.kind === 'vehicle') {
    const v = props.vehicles.find(item => item.id === hit.id);
    menu.title = vehicleCaption(hit.id, props.vehicles);
    menu.subtitle = v ? `${vehicleStatusLabel(v.status)} · 电量 ${Math.round(v.batteryPercent)}%` : '';
    const order = (props.orders || []).find(o => o.assignedVehicleId === hit.id && isLiveOrder(o));
    items.push({ key: 'focus-vehicle', label: '聚焦车辆', icon: 'i-carbon-center-circle' });
    items.push({
      key: followId.value === hit.id ? 'unfollow' : 'follow',
      label: followId.value === hit.id ? '取消跟随' : '跟随车辆',
      icon: 'i-carbon-user-follow'
    });
    items.push({
      key: 'view-order',
      label: order ? `查看任务 ${order.serial || order.orderId}` : '查看任务',
      icon: 'i-carbon-task',
      disabled: !order
    });
    items.push({ key: 'divider-1', label: '', divider: true });
    items.push({ key: 'copy-id', label: '复制车辆编号', icon: 'i-carbon-copy' });
  } else if (hit?.kind === 'node') {
    const entry = nodeIndex.get(hit.id);
    menu.title = entry ? nodeCaptionOf(entry) : hit.id;
    menu.subtitle = entry ? `${STATION_LABELS[entry.kind]} · ${hit.id}` : '';
    items.push({ key: 'focus-node', label: '聚焦站点', icon: 'i-carbon-center-circle' });
    if (props.enableTaskMenu) items.push({ key: 'create-task', label: '从此处新建导航任务', icon: 'i-carbon-add' });
    items.push({ key: 'divider-1', label: '', divider: true });
    items.push({ key: 'copy-id', label: '复制站点编号', icon: 'i-carbon-copy' });
    items.push({ key: 'copy-coord', label: '复制坐标', icon: 'i-carbon-copy', hint: `${world.x.toFixed(2)}, ${world.y.toFixed(2)}` });
  } else {
    menu.title = `X ${world.x.toFixed(2)}  Y ${world.y.toFixed(2)}`;
    menu.subtitle = nearest ? `最近站点 ${nodeCaptionOf(nodeIndex.get(nearest.id)!)}，约 ${(Math.sqrt(nearest.d2) / SCALE).toFixed(1)} m` : '';
    items.push({ key: 'focus-here', label: '聚焦此处', icon: 'i-carbon-center-circle' });
    if (zone) items.push({ key: 'focus-zone', label: `聚焦互斥区 ${zone.name}`, icon: 'i-carbon-map-boundary' });
    if (props.enableTaskMenu && nearest) items.push({ key: 'create-task', label: '从此处新建导航任务', icon: 'i-carbon-add' });
    items.push({ key: 'divider-1', label: '', divider: true });
    items.push({ key: 'copy-coord', label: '复制坐标', icon: 'i-carbon-copy' });
    items.push({ key: 'fit', label: '适配全图', icon: 'i-carbon-fit-to-screen', hint: 'F' });
    if (!northUp.value) items.push({ key: 'north', label: '回正北', icon: 'i-carbon-compass', hint: 'N' });
  }
  menu.items = items;
  menu.x = pt.x;
  menu.y = pt.y;
  menu.visible = true;
  rootSize.value = { w: camera.width, h: camera.height };
}

async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    area.remove();
  }
  showToast(`已复制 ${text}`);
}

function isLiveOrder(order: Fms.OrderDto) {
  return !['done', 'completed', 'failed', 'cancelled', 'canceled'].includes((order.status || '').toLowerCase());
}

function onMenuSelect(key: string) {
  const ctx = menuContext;
  closeMenu();
  if (!ctx) return;
  const hit = ctx.pick;
  switch (key) {
    case 'focus-vehicle':
      if (hit?.kind === 'vehicle') {
        emit('select-vehicle', hit.id);
        focusVehicle(hit.id, true);
      }
      break;
    case 'follow':
      if (hit?.kind === 'vehicle') {
        emit('select-vehicle', hit.id);
        followVehicle(hit.id);
        activePreset.value = 'follow';
      }
      break;
    case 'unfollow':
      stopFollow();
      activePreset.value = null;
      break;
    case 'view-order':
      if (hit?.kind === 'vehicle') {
        const order = (props.orders || []).find(o => o.assignedVehicleId === hit.id && isLiveOrder(o));
        emit('view-order', { vehicleId: hit.id, orderId: order?.orderId ?? null });
        if (order) focusOrder(order);
      }
      break;
    case 'focus-node':
      if (hit?.kind === 'node') {
        emit('select-node', hit.id);
        focusNode(hit.id);
      }
      break;
    case 'focus-here':
      flyTo({ x: ctx.world.x, y: ctx.world.y, scale: Math.max(curPxm(), 18) });
      addPulse(ctx.world.x * SCALE, -ctx.world.y * SCALE, 24, 1200, 1);
      break;
    case 'focus-zone':
      if (ctx.zoneId) focusZone(ctx.zoneId);
      break;
    case 'create-task': {
      const target = hit?.kind === 'node' ? hit.id : ctx.nearestNode;
      emit('request-create-task', { targetNodeId: target, x: ctx.world.x, y: ctx.world.y });
      break;
    }
    case 'copy-id':
      if (hit?.kind === 'vehicle' || hit?.kind === 'node') copyText(hit.id);
      break;
    case 'copy-coord':
      copyText(`${ctx.world.x.toFixed(2)}, ${ctx.world.y.toFixed(2)}`);
      break;
    case 'fit':
      applyPreset('global');
      break;
    case 'north':
      resetRotation();
      break;
    default:
      break;
  }
}

// ───────────────────────────── 键盘 ─────────────────────────────

const helpOpen = ref(false);

function isEditable(target: EventTarget | null) {
  const el = target as HTMLElement | null;
  if (!el || !el.tagName) return false;
  return el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable;
}

function mapHasFocus() {
  const root = rootRef.value;
  return !!root && (hovering.value || root.contains(document.activeElement));
}

function onKeyDown(e: KeyboardEvent) {
  if (!props.keyboard || e.ctrlKey || e.metaKey || e.altKey) return;
  if (isEditable(e.target) || !mapHasFocus()) return;
  const key = e.key;
  const lower = key.toLowerCase();

  if (key.startsWith('Arrow')) {
    e.preventDefault();
    markInteraction();
    camera.cancelFlight();
    inertia = null;
    heldKeys.add(key);
    if (e.shiftKey) heldKeys.add('Shift');
    return;
  }
  switch (true) {
    case lower === 'f':
      e.preventDefault();
      applyPreset('global');
      break;
    case key === '+' || key === '=':
      e.preventDefault();
      markInteraction();
      zoomIn();
      break;
    case key === '-' || key === '_':
      e.preventDefault();
      markInteraction();
      zoomOut();
      break;
    case lower === 'q':
      e.preventDefault();
      markInteraction();
      rotateBy(e.shiftKey ? -90 : -15);
      break;
    case lower === 'e':
      e.preventDefault();
      markInteraction();
      rotateBy(e.shiftKey ? 90 : 15);
      break;
    case lower === 'n':
      e.preventDefault();
      resetRotation();
      break;
    case key === '/' && props.showSearch:
      e.preventDefault();
      searchRef.value?.focus();
      break;
    case key === '?':
      e.preventDefault();
      helpOpen.value = !helpOpen.value;
      break;
    case key === 'Escape': {
      let handled = false;
      if (menu.visible) {
        closeMenu();
        handled = true;
      }
      if (helpOpen.value) {
        helpOpen.value = false;
        handled = true;
      }
      if (bookmarkOpen.value) {
        bookmarkOpen.value = false;
        handled = true;
      }
      if (followId.value) {
        stopFollow();
        activePreset.value = null;
        handled = true;
      }
      if (props.deselectable && (props.selectedVehicleId || props.selectedNodeId || props.selectedPathId)) {
        emit('select-vehicle', null);
        emit('select-node', null);
        emit('select-path', null);
        handled = true;
      }
      if (handled) e.preventDefault();
      break;
    }
    default:
      break;
  }
}

function onKeyUp(e: KeyboardEvent) {
  if (e.key.startsWith('Arrow')) heldKeys.delete(e.key);
  if (e.key === 'Shift') heldKeys.delete('Shift');
}

// ───────────────────────────── 搜索、提示、反馈 ─────────────────────────────

const nodeSearchItems = computed<SearchItem[]>(() =>
  (props.graph?.nodes || []).map(node => {
    const kind = stationKindOf(node.type);
    const name = node.name && node.name !== node.id ? node.name : '';
    return {
      kind: 'node' as const,
      id: node.id,
      title: name || node.id,
      subtitle: `${STATION_LABELS[kind]} · ${node.id}`,
      keys: [node.id.toLowerCase(), name.toLowerCase()]
    };
  })
);

const vehicleSearchItems = computed<SearchItem[]>(() =>
  (props.vehicles || []).map(v => ({
    kind: 'vehicle' as const,
    id: v.id,
    title: vehicleCaption(v.id, props.vehicles),
    subtitle: `${vehicleStatusLabel(v.status)} · 电量 ${Math.round(v.batteryPercent ?? 0)}%`,
    keys: [v.id.toLowerCase(), (v.name || '').toLowerCase(), vehicleCaption(v.id, props.vehicles).toLowerCase()]
  }))
);

const orderSearchItems = computed<SearchItem[]>(() =>
  (props.orders || []).map(order => ({
    kind: 'order' as const,
    id: order.orderId,
    title: order.serial || order.orderId,
    subtitle: `${orderStatusLabel(order.status)}${order.targetNodeId ? ` · ${order.sourceNodeId ? `${order.sourceNodeId} → ` : ''}${order.targetNodeId}` : ''}`,
    keys: [(order.serial || '').toLowerCase(), order.orderId.toLowerCase()]
  }))
);

const searchItems = computed<SearchItem[]>(() => [...vehicleSearchItems.value, ...orderSearchItems.value, ...nodeSearchItems.value]);

function onSearchPick(item: SearchItem) {
  markInteraction();
  if (item.kind === 'vehicle') {
    emit('select-vehicle', item.id);
    focusVehicle(item.id, true);
  } else if (item.kind === 'node') {
    emit('select-node', item.id);
    focusNode(item.id);
  } else {
    const order = (props.orders || []).find(o => o.orderId === item.id);
    if (order?.assignedVehicleId) emit('select-vehicle', order.assignedVehicleId);
    if (!order || !focusOrder(order)) showToast('这条任务暂时没有可显示的路径');
  }
  rootRef.value?.focus({ preventScroll: true });
}

const tip = computed(() => {
  const h = hover.value;
  if (!h || pointer) return null;
  const root = { w: camera.width, h: camera.height };
  let title = '';
  const lines: string[] = [];
  let color = '';
  if (h.kind === 'vehicle') {
    const state = vehicleSprites.get(h.id);
    const v = state?.data;
    if (!v) return null;
    title = vehicleCaption(v.id, props.vehicles);
    lines.push(`${vehicleStatusLabel(v.status)} · 电量 ${Math.round(v.batteryPercent ?? 0)}% · ${(v.currentSpeed ?? 0).toFixed(1)} m/s`);
    const order = (props.orders || []).find(o => o.assignedVehicleId === v.id && isLiveOrder(o));
    if (order) lines.push(`${order.serial || order.orderId}${order.targetNodeId ? ` → ${order.targetNodeId}` : ''}`);
    else lines.push('当前没有任务');
    color = cssColor(vehicleColor(v.status, themeStore.darkMode));
  } else {
    const entry = nodeIndex.get(h.id);
    if (!entry) return null;
    title = nodeCaptionOf(entry);
    lines.push(`${STATION_LABELS[entry.kind]} · ${h.id}`);
    lines.push(`X ${entry.node.x.toFixed(2)}  Y ${entry.node.y.toFixed(2)}`);
  }
  const width = 220;
  const height = 22 + lines.length * 17;
  let left = h.sx + 14;
  let top = h.sy + 16;
  if (left + width > root.w - 8) left = h.sx - width - 14;
  if (top + height > root.h - 8) top = h.sy - height - 12;
  return { left: Math.max(8, left), top: Math.max(8, top), title, lines, color };
});

const toastText = ref('');
let toastTimer = 0;

function showToast(text: string) {
  toastText.value = text;
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => {
    toastText.value = '';
  }, 2200);
}

function onMinimapJump(payload: { x: number; y: number; phase: 'start' | 'move' | 'end' }) {
  markInteraction();
  stopFollow();
  userMoved = true;
  activePreset.value = null;
  if (payload.phase === 'start') {
    camera.cancelFlight();
    zoomActive = false;
    inertia = null;
    startFlight({ cx: payload.x, cy: payload.y, scale: camera.scale, rotation: camera.rotation }, { duration: 260 });
    return;
  }
  camera.cancelFlight();
  camera.cx = payload.x;
  camera.cy = payload.y;
  touchCamera();
}

function onCompassClick() {
  markInteraction();
  if (northUp.value) {
    rotateBy(90);
    return;
  }
  resetRotation();
}

function toggleFollowHeading() {
  followHeading.value = !followHeading.value;
  if (!followHeading.value) {
    const target = camera.state();
    target.rotation = 0;
    startFlight(target, { duration: 420 }, true);
  }
}

function toggleCruise() {
  setCruise(!cruiseOn.value);
}

function toggleBookmarks() {
  bookmarkOpen.value = !bookmarkOpen.value;
  helpOpen.value = false;
}

function focusFirstConflict() {
  const first = (props.conflicts || [])[0];
  if (first) focusConflict(first);
}

const followChip = computed(() => {
  if (!followId.value || followSoftUntil > 0) return '';
  return vehicleCaption(followId.value, props.vehicles);
});

defineExpose({
  focusVehicle,
  focusNode,
  focusOrder,
  focusZone,
  focusConflict,
  focusPoint,
  flyTo,
  fitView: fitToScreen,
  fitToScreen,
  resetView,
  zoomIn,
  zoomOut,
  rotateBy,
  resetRotation,
  followVehicle,
  stopFollow,
  applyPreset,
  saveBookmark,
  focusFirstConflict,
  getCamera: () => ({
    x: camera.cx / SCALE,
    y: -camera.cy / SCALE,
    scale: curPxm(),
    rotation: (normalizeAngle(camera.rotation) * 180) / Math.PI
  })
});

void lastTickTs;
void nextTick;
</script>

<template>
  <div
    ref="rootRef"
    tabindex="0"
    class="relative h-full w-full select-none overflow-hidden bg-[#f7f8fa] outline-none dark:bg-[#101418]"
    @mouseenter="hovering = true"
    @mouseleave="hovering = false"
  >
    <!-- PixiJS 画布 -->
    <div ref="hostRef" class="h-full w-full"></div>

    <!-- 搜索 -->
    <div v-if="showSearch" class="absolute z-20" :style="{ left: `${hud.left}px`, top: `${hud.top}px` }">
      <MapSearchBox ref="searchRef" :items="searchItems" @pick="onSearchPick" />
    </div>

    <!-- 视角工具栏 -->
    <div
      v-if="showToolbar"
      class="absolute z-20 flex items-center gap-1 rounded-md border border-slate-200 bg-white/95 p-1 text-[12px] text-slate-700 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95 dark:text-slate-200"
      :style="{ right: `${hud.right}px`, top: `${hud.top}px` }"
      @pointerdown.stop
    >
      <button
        v-for="item in [
          { key: 'global', label: '全局', icon: 'i-carbon-map' },
          { key: 'follow', label: '跟随', icon: 'i-carbon-user-follow' },
          { key: 'task', label: '任务', icon: 'i-carbon-task' },
          { key: 'alert', label: '异常', icon: 'i-carbon-warning-alt' }
        ] as const"
        :key="item.key"
        type="button"
        class="flex h-6 items-center gap-1 rounded px-2 transition-colors"
        :class="
          activePreset === item.key
            ? 'bg-blue-50 text-blue-700 dark:bg-blue-500/20 dark:text-blue-300'
            : 'hover:bg-slate-100 dark:hover:bg-white/10'
        "
        :title="`视角预设：${item.label}`"
        @click="applyPreset(item.key)"
      >
        <div :class="item.icon" class="text-13px" />
        {{ item.label }}
      </button>
      <div class="mx-0.5 h-4 w-px bg-slate-200 dark:bg-slate-700" />
      <div class="relative">
        <button
          type="button"
          class="flex h-6 items-center gap-1 rounded px-2 transition-colors hover:bg-slate-100 dark:hover:bg-white/10"
          :class="bookmarkOpen ? 'bg-slate-100 dark:bg-white/10' : ''"
          title="视角书签"
          @click="toggleBookmarks"
        >
          <div class="i-carbon-bookmark text-13px" />
          书签
        </button>
        <div
          v-if="bookmarkOpen"
          class="absolute right-0 top-full z-30 mt-2 w-[248px] rounded-md border border-slate-200 bg-white/98 p-1.5 shadow-xl dark:border-slate-700 dark:bg-[#151b22]/98"
        >
          <button
            type="button"
            class="flex h-8 w-full items-center gap-2 rounded px-2 text-left text-blue-700 hover:bg-blue-50 dark:text-blue-300 dark:hover:bg-blue-500/15"
            @click="saveBookmark"
          >
            <div class="i-carbon-bookmark-add text-14px" />
            保存当前视角
          </button>
          <div v-if="bookmarks.length" class="my-1 h-px bg-slate-100 dark:bg-slate-800" />
          <div v-for="mark in bookmarks" :key="mark.id" class="group flex items-center rounded hover:bg-slate-50 dark:hover:bg-white/5">
            <button type="button" class="flex min-w-0 flex-1 items-center gap-2 px-2 py-1.5 text-left" @click="gotoBookmark(mark)">
              <div class="i-carbon-bookmark-filled shrink-0 text-13px text-slate-400" />
              <span class="min-w-0 flex-1">
                <span class="block truncate">{{ mark.name }}</span>
                <span class="block truncate text-[10.5px] text-slate-400">
                  {{ mark.x.toFixed(0) }}, {{ mark.y.toFixed(0) }} · {{ mark.scale.toFixed(0) }} px/m
                </span>
              </span>
            </button>
            <button
              type="button"
              class="mr-1 flex h-6 w-6 items-center justify-center rounded text-slate-400 opacity-0 transition-opacity hover:bg-slate-100 hover:text-red-600 group-hover:opacity-100 dark:hover:bg-white/10"
              title="删除书签"
              @click="removeBookmark(mark.id)"
            >
              <div class="i-carbon-close text-12px" />
            </button>
          </div>
          <div v-if="!bookmarks.length" class="px-2 py-2 text-[11px] text-slate-400">保存后可在这里一键回到该视角</div>
        </div>
      </div>
      <button
        v-if="showCruise"
        type="button"
        class="flex h-6 items-center gap-1 rounded px-2 transition-colors"
        :class="cruiseOn ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300' : 'hover:bg-slate-100 dark:hover:bg-white/10'"
        title="巡航：在运行中的车辆之间自动切换视角"
        @click="toggleCruise"
      >
        <div :class="cruiseOn ? 'i-carbon-pause-filled' : 'i-carbon-play-filled-alt'" class="text-12px" />
        巡航
      </button>
      <button
        type="button"
        class="flex h-6 w-6 items-center justify-center rounded transition-colors hover:bg-slate-100 dark:hover:bg-white/10"
        :class="helpOpen ? 'bg-slate-100 dark:bg-white/10' : ''"
        title="快捷键 (?)"
        @click="helpOpen = !helpOpen; bookmarkOpen = false"
      >
        <div class="i-carbon-keyboard text-14px" />
      </button>
    </div>

    <!-- 跟随与巡航状态 -->
    <div
      v-if="followChip || cruiseChip"
      class="absolute left-1/2 z-20 flex -translate-x-1/2 flex-col items-center gap-1.5"
      :style="{ top: `${hud.top + (showToolbar || showSearch ? 44 : 0)}px` }"
    >
      <div
        v-if="followChip"
        class="flex items-center gap-2 rounded-full border border-blue-200 bg-white/95 py-1 pl-3 pr-1.5 text-[12px] text-slate-700 shadow-sm dark:border-blue-500/40 dark:bg-[#151b22]/95 dark:text-slate-200"
      >
        <span class="h-1.5 w-1.5 rounded-full bg-blue-500" />
        正在跟随 {{ followChip }}
        <button
          type="button"
          class="rounded px-1.5 py-0.5 text-[11px] transition-colors"
          :class="followHeading ? 'bg-blue-50 text-blue-700 dark:bg-blue-500/20 dark:text-blue-300' : 'text-slate-500 hover:bg-slate-100 dark:hover:bg-white/10'"
          title="地图旋转到车头朝上"
          @click="toggleFollowHeading"
        >
          车头朝上
        </button>
        <button
          type="button"
          class="flex h-5 w-5 items-center justify-center rounded-full text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/10"
          title="退出跟随 (Esc)"
          @click="stopFollow(); activePreset = null"
        >
          <div class="i-carbon-close text-12px" />
        </button>
      </div>
      <div
        v-if="cruiseChip"
        class="flex items-center gap-2 rounded-full border border-emerald-200 bg-white/95 px-3 py-1 text-[12px] text-slate-700 shadow-sm dark:border-emerald-500/40 dark:bg-[#151b22]/95 dark:text-slate-200"
      >
        <span class="h-1.5 w-1.5 rounded-full" :class="cruisePauseLeft > 0 ? 'bg-amber-500' : 'animate-pulse bg-emerald-500'" />
        {{ cruiseChip }}
      </div>
    </div>

    <!-- 鹰眼 -->
    <div v-if="showNavigator" class="absolute z-10" :style="{ left: `${hud.left}px`, bottom: `${hud.bottom + 40}px` }">
      <MapMiniMap
        :graph="graph"
        :vehicles="vehicles"
        :dark="themeStore.darkMode"
        :quad="viewQuad"
        :selected-vehicle-id="selectedVehicleId"
        @jump="onMinimapJump"
      />
    </div>

    <!-- 右下角视图控制 -->
    <div
      class="absolute z-10 flex flex-col gap-0.5 rounded-md border border-slate-200 bg-white/95 p-1 text-12px text-slate-700 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95 dark:text-slate-200"
      :style="{ right: `${hud.right}px`, bottom: `${hud.bottom}px` }"
      @pointerdown.stop
    >
      <button
        :title="northUp ? '指北针：点击顺时针旋转 90°' : '回正北 (N)'"
        class="relative h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
        @click="onCompassClick"
      >
        <svg
          viewBox="-16 -16 32 32"
          width="24"
          height="24"
          class="transition-transform duration-75"
          :style="{ transform: `rotate(${rotationDeg}deg)` }"
        >
          <circle r="13.5" fill="none" stroke="currentColor" stroke-opacity="0.28" stroke-width="1.2" />
          <path d="M0 -11 L3.8 1.5 L0 -0.8 L-3.8 1.5 Z" fill="#d23a52" />
          <path d="M0 11 L3.8 -1.5 L0 0.8 L-3.8 -1.5 Z" fill="currentColor" fill-opacity="0.38" />
        </svg>
      </button>
      <div class="my-0.5 h-[1px] bg-slate-200/80 dark:bg-slate-700/50"></div>
      <button
        title="放大 (+)"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
        @click="zoomIn"
      >
        <div class="i-carbon-zoom-in text-16px"></div>
      </button>
      <button
        title="缩小 (-)"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
        @click="zoomOut"
      >
        <div class="i-carbon-zoom-out text-16px"></div>
      </button>
      <div class="my-0.5 h-[1px] bg-slate-200/80 dark:bg-slate-700/50"></div>
      <button
        title="适应视图 (F)"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
        @click="fitToScreen(true)"
      >
        <div class="i-carbon-fit-to-screen text-16px"></div>
      </button>
      <button
        title="重置视角 (1:1)"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
        @click="resetView"
      >
        <div class="i-carbon-restart text-16px"></div>
      </button>
      <template v-if="showToolbar">
        <div class="my-0.5 h-[1px] bg-slate-200/80 dark:bg-slate-700/50"></div>
        <button
          title="逆时针旋转 90° (Shift+Q)"
          class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
          @click="rotateBy(-90)"
        >
          <div class="i-carbon-rotate-counterclockwise text-16px"></div>
        </button>
        <button
          title="顺时针旋转 90° (Shift+E)"
          class="h-8 w-8 flex-center rounded-lg text-slate-700 transition-all hover:bg-slate-100 active:scale-90 dark:text-slate-200 dark:hover:bg-white/10"
          @click="rotateBy(90)"
        >
          <div class="i-carbon-rotate-clockwise text-16px"></div>
        </button>
      </template>
    </div>

    <!-- 左下角比例尺与坐标 -->
    <div
      class="absolute z-10 flex items-center gap-3 rounded-md border border-slate-200 bg-white/95 px-2.5 py-1.5 font-mono text-[11px] tabular-nums text-slate-600 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95 dark:text-slate-300"
      :style="{ left: `${hud.left}px`, bottom: `${hud.bottom}px` }"
    >
      <span class="inline-block h-px bg-slate-700 align-middle dark:bg-slate-200" :style="{ width: `${scaleBar.px}px` }"></span>
      <span>{{ scaleBar.label }}</span>
      <span>X {{ cursorWorld.x.toFixed(2) }}</span>
      <span>Y {{ cursorWorld.y.toFixed(2) }}</span>
      <span class="text-slate-400">{{ displayZoom }}%</span>
      <span class="text-slate-400">{{ pxPerMeterLabel }} px/m · {{ tierName }}</span>
    </div>

    <!-- 悬停提示 -->
    <div
      v-if="tip"
      class="pointer-events-none absolute z-30 w-[220px] rounded-md border border-slate-200 bg-white/98 px-2.5 py-1.5 text-[12px] leading-[17px] text-slate-600 shadow-lg dark:border-slate-700 dark:bg-[#151b22]/98 dark:text-slate-300"
      :style="{ left: `${tip.left}px`, top: `${tip.top}px` }"
    >
      <div class="flex items-center gap-1.5 font-medium text-slate-900 dark:text-slate-100">
        <span v-if="tip.color" class="h-2 w-2 rounded-sm" :style="{ background: tip.color }" />
        {{ tip.title }}
      </div>
      <div v-for="line in tip.lines" :key="line" class="truncate">{{ line }}</div>
    </div>

    <MapContextMenu
      :visible="menu.visible"
      :x="menu.x"
      :y="menu.y"
      :title="menu.title"
      :subtitle="menu.subtitle"
      :items="menu.items"
      :container-width="rootSize.w"
      :container-height="rootSize.h"
      @select="onMenuSelect"
      @close="closeMenu"
    />

    <MapShortcutsHelp :visible="helpOpen" @close="helpOpen = false" />

    <Transition name="map-toast">
      <div
        v-if="toastText"
        class="pointer-events-none absolute bottom-16 left-1/2 z-40 -translate-x-1/2 rounded-md bg-slate-900/90 px-3 py-1.5 text-[12px] text-white shadow-lg dark:bg-slate-100/95 dark:text-slate-900"
      >
        {{ toastText }}
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.map-toast-enter-active,
.map-toast-leave-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s ease;
}
.map-toast-enter-from,
.map-toast-leave-to {
  opacity: 0;
  transform: translate(-50%, 6px);
}
</style>
