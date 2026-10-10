<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { Application, Circle, Container, Graphics, Sprite, Text, TextStyle, Texture } from 'pixi.js';
import { useThemeStore } from '@/store/modules/theme';

const themeStore = useThemeStore();

const props = withDefaults(
  defineProps<{
    graph?: Fms.MapGraphDto | null;
    vehicles?: Fms.VehicleDto[];
    mutexZones?: Fms.MutexZoneDto[];
    /** 在途任务：用于在地图上画出车辆剩余路线与目的地 */
    orders?: Fms.OrderDto[];
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
    };
  }>(),
  {
    graph: null,
    vehicles: () => [],
    mutexZones: () => [],
    orders: () => [],
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
    hudInset: () => ({})
  }
);

const hud = computed(() => ({
  left: props.hudInset?.left ?? 16,
  right: props.hudInset?.right ?? 16,
  bottom: props.hudInset?.bottom ?? 16
}));

const emit = defineEmits<{
  (e: 'select-vehicle', id: string | null): void;
  (e: 'select-node', id: string | null): void;
  (e: 'select-path', id: string | null): void;
  (e: 'follow-change', id: string | null): void;
}>();

// Scale: 1 meter = 40 pixels by default
const SCALE = 40;

const canvasContainerRef = ref<HTMLDivElement | null>(null);
const cursorWorldPos = ref<Fms.Point2D>({ x: 0, y: 0 });
const zoomLevel = ref(1.0);
const fittedScale = ref(1);
const displayZoom = computed(() => Math.round((zoomLevel.value / (fittedScale.value || 1)) * 100));
const scaleBar = computed(() => {
  const pxPerMeter = SCALE * (zoomLevel.value || 1);
  const candidates = [0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500];
  const target = 88 / Math.max(pxPerMeter, 0.001);
  const meters = candidates.reduce((best, item) => (Math.abs(Math.log(item / target)) < Math.abs(Math.log(best / target)) ? item : best));
  return {
    px: Math.round(Math.max(28, Math.min(140, meters * pxPerMeter))),
    label: meters >= 1 ? `${meters} m` : `${Math.round(meters * 100)} cm`
  };
});

// PixiJS objects
let app: Application | null = null;
let worldContainer: Container | null = null;
let gridLayer: Graphics | null = null;
let lidarLayer: Graphics | null = null;
let rasterSprite: Sprite | null = null;
let cloudSprite: Sprite | null = null;
let cloudKey = '';
let rasterKey = '';
let mutexLayer: Graphics | null = null;
let pathLayer: Graphics | null = null;
let nodeLayer: Container | null = null;
let labelLayer: Container | null = null;
let taskLayer: Graphics | null = null;
let vehicleLayer: Container | null = null;

function viewScale() {
  return Math.max(0.02, worldContainer?.scale.x || 1);
}

/** 把屏幕像素换算成当前缩放下的世界像素，避免放大后线条和文字一起变粗。 */
function ink(screenPixels: number) {
  return screenPixels / viewScale();
}

/** 站点编号从中等缩放开始显示（8 px/m，落在 8–12 区间）。 */
const STATION_LABEL_PX_PER_M = 8;
const STATIC_REBUILD_MS = 150;
const LABEL_CREATE_BUDGET = 40;
const LABEL_MAX_VISIBLE = 180;

function zoomLod() {
  const pxm = SCALE * viewScale();
  if (pxm < 4) return 0;
  if (pxm < 10) return 1;
  if (pxm < 16) return 2;
  return 3;
}

let staticTimer = 0;
let drawnLod = -1;
let labelsDirty = false;

interface NodeLabelState {
  holder: Container;
  label: Text;
}

const nodeLabels = new Map<string, NodeLabelState>();

function scheduleStaticRebuild() {
  window.clearTimeout(staticTimer);
  staticTimer = window.setTimeout(flushStaticRebuild, STATIC_REBUILD_MS);
}

function flushStaticRebuild() {
  if (!worldContainer) return;
  const lod = zoomLod();
  if (lod === drawnLod) return;
  renderStaticMap();
  drawnLod = lod;
  labelsDirty = true;
}

function invalidateStatic() {
  drawnLod = -1;
  window.clearTimeout(staticTimer);
  if (worldContainer) {
    renderStaticMap();
    drawnLod = zoomLod();
    labelsDirty = true;
  }
}

function clearNodeLabels() {
  for (const state of nodeLabels.values()) {
    state.holder.destroy({ children: true });
  }
  nodeLabels.clear();
}

interface VehicleRenderState {
  currentPose: Fms.Pose2D;
  targetPose: Fms.Pose2D;
  container: Container;
  graphics: Graphics;
  label: Text;
  laserScan: Graphics;
  statusBadge: Graphics;
}
const vehicleSprites = new Map<string, VehicleRenderState>();

let isDragging = false;
let dragStartPointer = { x: 0, y: 0 };
let dragStartWorld = { x: 0, y: 0 };
let userMoved = false;
let followId: string | null = null;
let resizeObserver: ResizeObserver | null = null;

onMounted(async () => {
  if (!canvasContainerRef.value) return;

  app = new Application();
  const initialBg = themeStore.darkMode ? 0x101418 : 0xf7f8fa;
  await app.init({
    resizeTo: canvasContainerRef.value,
    antialias: true,
    backgroundColor: initialBg,
    resolution: Math.max(2, window.devicePixelRatio || 1),
    autoDensity: true,
    powerPreference: 'high-performance'
  });

  canvasContainerRef.value.appendChild(app.canvas);

  worldContainer = new Container();
  app.stage.addChild(worldContainer);

  gridLayer = new Graphics();
  lidarLayer = new Graphics();
  rasterSprite = new Sprite();
  cloudSprite = new Sprite();
  rasterSprite.visible = false;
  cloudSprite.visible = false;
  mutexLayer = new Graphics();
  pathLayer = new Graphics();
  nodeLayer = new Container();
  labelLayer = new Container();
  labelLayer.eventMode = 'none';
  taskLayer = new Graphics();
  vehicleLayer = new Container();

  worldContainer.addChild(gridLayer);
  worldContainer.addChild(rasterSprite);
  worldContainer.addChild(lidarLayer);
  worldContainer.addChild(cloudSprite);
  worldContainer.addChild(mutexLayer);
  worldContainer.addChild(pathLayer);
  worldContainer.addChild(nodeLayer);
  worldContainer.addChild(labelLayer);
  worldContainer.addChild(taskLayer);
  worldContainer.addChild(vehicleLayer);

  worldContainer.position.set(app.screen.width / 2, app.screen.height / 2);

  setupEvents();
  renderGrid();

  if (props.graph) {
    renderStaticMap();
    drawnLod = zoomLod();
    fitToScreen();
  }

  resizeObserver = new ResizeObserver(() => {
    if (!userMoved) fitToScreen();
  });
  resizeObserver.observe(canvasContainerRef.value);

  // 60FPS LERP animation loop
  app.ticker.add(ticker => {
    updateVehicleAnimation(ticker.deltaTime);
    updateFollowCamera();
    drawTaskOverlay(ticker.deltaTime);
    if (labelsDirty) updateNodeLabels();
  });
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  resizeObserver = null;
  window.clearTimeout(staticTimer);
  clearNodeLabels();
  if (app) {
    app.destroy(true, { children: true });
    app = null;
  }
});

watch(
  () => props.graph,
  () => {
    clearNodeLabels();
    invalidateStatic();
    if (!userMoved) fitToScreen();
  },
  { deep: true }
);

watch(
  () => props.layerVisibility,
  () => {
    invalidateStatic();
  },
  { deep: true }
);

watch(
  () => props.selectedNodeId,
  () => {
    labelsDirty = true;
  }
);

watch(
  () => props.selectedVehicleId,
  () => {
    syncVehicles(props.vehicles || []);
  }
);

watch(
  () => props.raster?.url,
  () => {
    rasterKey = '';
    renderRaster();
    if (!userMoved) fitToScreen();
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
  () => props.mutexZones,
  () => {
    invalidateStatic();
  },
  { deep: true }
);

watch(
  () => props.vehicles,
  newVehicles => {
    syncVehicles(newVehicles);
  },
  { deep: true }
);

watch(
  () => themeStore.darkMode,
  isDark => {
    if (app) {
      app.renderer.background.color = isDark ? 0x101418 : 0xf7f8fa;
      renderGrid();
      clearNodeLabels();
      invalidateStatic();
      syncVehicles(props.vehicles || []);
    }
  }
);

function setupEvents() {
  if (!app) return;
  const canvas = app.canvas;

  canvas.addEventListener('pointerdown', (e: PointerEvent) => {
    if (e.button === 0 || e.button === 1) {
      isDragging = true;
      dragStartPointer = { x: e.clientX, y: e.clientY };
      if (worldContainer) {
        dragStartWorld = { x: worldContainer.position.x, y: worldContainer.position.y };
      }
    }
  });

  window.addEventListener('pointermove', (e: PointerEvent) => {
    if (isDragging && worldContainer) {
      const dx = e.clientX - dragStartPointer.x;
      const dy = e.clientY - dragStartPointer.y;
      if (Math.abs(dx) > 3 || Math.abs(dy) > 3) {
        userMoved = true;
        if (followId) followVehicle(null);
      }
      worldContainer.position.set(dragStartWorld.x + dx, dragStartWorld.y + dy);
      renderGrid();
      labelsDirty = true;
    }

    if (worldContainer?.position && worldContainer?.scale && canvas) {
      const rect = canvas.getBoundingClientRect();
      const screenX = e.clientX - rect.left;
      const screenY = e.clientY - rect.top;
      const worldX = (screenX - worldContainer.position.x) / (SCALE * worldContainer.scale.x);
      const worldY = -(screenY - worldContainer.position.y) / (SCALE * worldContainer.scale.y);
      cursorWorldPos.value = { x: worldX, y: worldY };
    }
  });

  window.addEventListener('pointerup', () => {
    isDragging = false;
  });

  canvas.addEventListener(
    'wheel',
    (e: WheelEvent) => {
      e.preventDefault();
      if (!worldContainer) return;
      userMoved = true;

      const zoomFactor = e.deltaY < 0 ? 1.15 : 0.87;
      const oldScale = worldContainer.scale.x;
      let newScale = oldScale * zoomFactor;
      newScale = Math.max(0.02, Math.min(10.0, newScale));

      const rect = canvas.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      const worldPos = {
        x: (mouseX - worldContainer.position.x) / oldScale,
        y: (mouseY - worldContainer.position.y) / oldScale
      };

      worldContainer.scale.set(newScale);
      worldContainer.position.x = mouseX - worldPos.x * newScale;
      worldContainer.position.y = mouseY - worldPos.y * newScale;

      zoomLevel.value = newScale;
      renderGrid();
      labelsDirty = true;
      updateNodeLabels();
      scheduleStaticRebuild();
    },
    { passive: false }
  );
}

function renderGrid() {
  if (!gridLayer || !app || !worldContainer) return;
  gridLayer.clear();

  const scale = viewScale();
  const left = -worldContainer.position.x / scale;
  const top = -worldContainer.position.y / scale;
  const right = left + app.screen.width / scale;
  const bottom = top + app.screen.height / scale;
  const targetMeters = 64 / (SCALE * scale);
  const steps = [0.5, 1, 2, 5, 10, 20, 50, 100];
  const meters = steps.find(step => step >= targetMeters * 0.8) ?? 100;
  const step = meters * SCALE;
  const majorEvery = 5;

  const isDark = themeStore.darkMode;
  const minorColor = isDark ? 0x1a212a : 0xeef1f4;
  const majorColor = isDark ? 0x2a3542 : 0xdfe5eb;
  const x0 = Math.floor(left / step) * step;
  const y0 = Math.floor(top / step) * step;

  const strokeAt = (color: number, alpha: number) => {
    gridLayer!.setStrokeStyle({ width: ink(1), color, alpha, cap: 'butt' });
  };

  strokeAt(minorColor, isDark ? 0.95 : 1);
  for (let x = x0, i = Math.round(x0 / step); x <= right; x += step, i += 1) {
    if (i % majorEvery === 0) continue;
    gridLayer.moveTo(x, top).lineTo(x, bottom);
  }
  for (let y = y0, i = Math.round(y0 / step); y <= bottom; y += step, i += 1) {
    if (i % majorEvery === 0) continue;
    gridLayer.moveTo(left, y).lineTo(right, y);
  }
  gridLayer.stroke();

  strokeAt(majorColor, 1);
  for (let x = x0, i = Math.round(x0 / step); x <= right; x += step, i += 1) {
    if (i % majorEvery !== 0) continue;
    gridLayer.moveTo(x, top).lineTo(x, bottom);
  }
  for (let y = y0, i = Math.round(y0 / step); y <= bottom; y += step, i += 1) {
    if (i % majorEvery !== 0) continue;
    gridLayer.moveTo(left, y).lineTo(right, y);
  }
  gridLayer.stroke();
}

function renderRaster() {
  if (!rasterSprite) return;
  const show = props.layerVisibility.raster !== false && Boolean(props.raster?.url);
  rasterSprite.visible = show;
  if (!show || !props.raster || rasterKey === props.raster.url) return;
  rasterKey = props.raster.url;
  const image = new Image();
  image.onload = () => {
    if (!rasterSprite || props.raster?.url !== rasterKey) return;
    rasterSprite.texture = Texture.from(image);
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
    minX = Math.min(minX, point.x);
    minY = Math.min(minY, point.y);
    maxX = Math.max(maxX, point.x);
    maxY = Math.max(maxY, point.y);
  }
  const spanX = Math.max(0.05, maxX - minX);
  const spanY = Math.max(0.05, maxY - minY);
  const pixelsPerMeter = Math.min(4096 / spanX, 4096 / spanY, Math.max(12, SCALE * viewScale()));
  const key = `${points.length}:${pixelsPerMeter.toFixed(1)}:${points[0]?.x}:${points[points.length - 1]?.x}`;
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
  for (const point of points) {
    const px = Math.min(width - 1, Math.max(0, Math.floor((point.x - minX) * pixelsPerMeter)));
    const py = Math.min(height - 1, Math.max(0, Math.floor((maxY - point.y) * pixelsPerMeter)));
    const offset = (py * width + px) * 4;
    image.data[offset] = 71;
    image.data[offset + 1] = 85;
    image.data[offset + 2] = 105;
    image.data[offset + 3] = 170;
  }
  context.putImageData(image, 0, 0);
  cloudSprite.texture = Texture.from(canvas);
  cloudSprite.x = minX * SCALE;
  cloudSprite.y = -maxY * SCALE;
  cloudSprite.width = spanX * SCALE;
  cloudSprite.height = spanY * SCALE;
}

function renderStaticMap() {
  if (!props.graph || !pathLayer || !nodeLayer || !mutexLayer) return;

  const graph = props.graph;
  const vis = props.layerVisibility;
  const isDark = themeStore.darkMode;

  pathLayer.clear();
  mutexLayer.clear();
  nodeLayer.removeChildren();

  // 0. Kecong raster and baked lidar. These stay put while zones and vehicles move.
  renderRaster();
  renderPointCloud();

  // 1. Mutex Zones (支持多边形 posgroup 拓扑锁区与矩形兜底)
  if (vis.mutexZones && graph.mutexZones) {
    for (const zone of graph.mutexZones) {
      const liveZone = props.mutexZones?.find(z => z.zoneId === zone.zoneId) || zone;
      const isOccupied = liveZone.currentOccupantVehicleIds && liveZone.currentOccupantVehicleIds.length > 0;
      const fillColor = isOccupied ? 0xd03050 : 0xf59e0b;
      const strokeColor = isOccupied ? 0xef4444 : 0xf59e0b;

      if (zone.polygon && zone.polygon.length >= 3) {
        const polyPoints = zone.polygon.map(pt => ({ x: pt.x * SCALE, y: -pt.y * SCALE }));
        mutexLayer.poly(polyPoints);
        mutexLayer.fill({
          color: fillColor,
          alpha: isOccupied ? 0.18 : 0.04
        });
        mutexLayer.stroke({
          width: ink(isOccupied ? 1.75 : 1),
          color: strokeColor,
          alpha: isOccupied ? 0.9 : 0.55
        });
      } else {
        const memberNodes = graph.nodes.filter(n => zone.memberNodeIds?.includes(n.id));
        if (memberNodes.length > 0) {
          let minX = Infinity;
          let minY = Infinity;
          let maxX = -Infinity;
          let maxY = -Infinity;
          for (const n of memberNodes) {
            minX = Math.min(minX, n.x);
            minY = Math.min(minY, n.y);
            maxX = Math.max(maxX, n.x);
            maxY = Math.max(maxY, n.y);
          }
          const pad = 1.2;
          const px = (minX - pad) * SCALE;
          const py = -(maxY + pad) * SCALE;
          const pw = (maxX - minX + 2 * pad) * SCALE;
          const ph = (maxY - minY + 2 * pad) * SCALE;

          mutexLayer.rect(px, py, pw, ph);
          mutexLayer.fill({
            color: fillColor,
            alpha: isOccupied ? 0.16 : 0.035
          });
          mutexLayer.stroke({
            width: ink(isOccupied ? 1.75 : 1),
            color: strokeColor,
            alpha: isOccupied ? 0.9 : 0.55
          });
        }
      }
    }
  }

  const nodeById = new Map(graph.nodes.map(node => [node.id, node]));
  const pxPerMeter = SCALE * viewScale();
  const showWayMarks = pxPerMeter >= 4;
  const dotSlots = new Set<string>();
  const drawnSegments = new Set<string>();
  const inkColor = isDark ? 0xd5dee8 : 0x243140;
  const edgeColor = isDark ? 0x3a4654 : 0xc5ced6;

  for (const path of graph.paths) {
    const isCurve = path.pathType === 'CubicBezier' && path.controlPoints && path.controlPoints.length >= 2;
    if (isCurve && !vis.bezierPaths) continue;
    if (!isCurve && !vis.straightPaths) continue;

    const startNode = nodeById.get(path.startNodeId);
    const endNode = nodeById.get(path.endNodeId);
    if (!startNode || !endNode) continue;

    const segmentKey = [path.startNodeId, path.endNodeId].sort().join('|') + (isCurve ? '|c' : '|s');
    if (drawnSegments.has(segmentKey)) continue;
    drawnSegments.add(segmentKey);

    const x0 = startNode.x * SCALE;
    const y0 = -startNode.y * SCALE;
    const x3 = endNode.x * SCALE;
    const y3 = -endNode.y * SCALE;
    const chord = Math.hypot(x3 - x0, y3 - y0) * viewScale();
    if (chord < 1.2 && props.selectedPathId !== path.id) continue;

    const selected = props.selectedPathId === path.id;
    const pathColor = selected ? 0x9a3412 : inkColor;
    let x1 = x0;
    let y1 = y0;
    let x2 = x3;
    let y2 = y3;
    if (isCurve && path.controlPoints) {
      x1 = path.controlPoints[0].x * SCALE;
      y1 = -path.controlPoints[0].y * SCALE;
      x2 = path.controlPoints[1].x * SCALE;
      y2 = -path.controlPoints[1].y * SCALE;
    }

    const samples = isCurve ? sampleBezier(x0, y0, x1, y1, x2, y2, x3, y3, 18) : [{ x: x0, y: y0 }, { x: x3, y: y3 }];
    const roadWidth = path.roadWidth && path.roadWidth > 0 ? path.roadWidth : 1.2;
    if (roadWidth * pxPerMeter >= 7) {
      const edges = offsetPolyline(samples, (roadWidth * SCALE) / 2);
      strokePolyline(pathLayer, edges.left, { width: ink(1), color: edgeColor, alpha: 1 });
      strokePolyline(pathLayer, edges.right, { width: ink(1), color: edgeColor, alpha: 1 });
    }

    strokePolyline(pathLayer, samples, {
      width: ink(selected ? 2 : 1.15),
      color: pathColor,
      alpha: 1,
      cap: 'butt'
    });

    if (showArrows(vis.arrows, chord)) {
      const mid = samples[Math.floor(samples.length / 2)];
      const prev = samples[Math.max(0, Math.floor(samples.length / 2) - 1)];
      const angle = Math.atan2(mid.y - prev.y, mid.x - prev.x);
      drawArrowHead(pathLayer, mid.x, mid.y, path.direction === 'backward' ? angle + Math.PI : angle, pathColor);
    }
  }

  if (vis.nodes) {
    for (const node of graph.nodes) {
      const selected = props.selectedNodeId === node.id;
      const kind = stationKind(node);
      const nx = node.x * SCALE;
      const ny = -node.y * SCALE;
      const nodeContainer = new Container();
      nodeContainer.position.set(nx, ny);
      nodeContainer.eventMode = 'static';
      nodeContainer.cursor = 'pointer';
      nodeContainer.hitArea = new Circle(0, 0, ink(8));
      nodeContainer.on('pointerdown', event => {
        event.stopPropagation();
        emit('select-node', node.id);
      });

      const landmark = kind === 'job' || kind === 'charge' || kind === 'park';
      const drawMark = landmark || selected || showWayMarks;
      if (drawMark) {
        const slot = `${Math.round(nx / ink(6))}:${Math.round(ny / ink(6))}`;
        const crowded = !landmark && !selected && dotSlots.has(slot);
        if (!crowded) {
          const mark = new Graphics();
          drawStationMark(mark, kind, selected, isDark);
          nodeContainer.addChild(mark);
          dotSlots.add(slot);
        }
      }

      nodeLayer.addChild(nodeContainer);
    }
  }
}

function showArrows(enabled: boolean | undefined, chord: number) {
  return Boolean(enabled) && chord >= 28 && SCALE * viewScale() >= 8;
}

function sampleBezier(x0: number, y0: number, x1: number, y1: number, x2: number, y2: number, x3: number, y3: number, steps: number) {
  const points: Array<{ x: number; y: number }> = [];
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

function offsetPolyline(points: Array<{ x: number; y: number }>, distance: number) {
  const left: Array<{ x: number; y: number }> = [];
  const right: Array<{ x: number; y: number }> = [];
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

function strokePolyline(
  graphics: Graphics,
  points: Array<{ x: number; y: number }>,
  style: { width: number; color: number; alpha: number; cap?: 'butt' | 'round' }
) {
  if (points.length < 2) return;
  graphics.setStrokeStyle({ cap: 'butt', join: 'miter', ...style });
  graphics.moveTo(points[0].x, points[0].y);
  for (let i = 1; i < points.length; i += 1) graphics.lineTo(points[i].x, points[i].y);
  graphics.stroke();
}

function nodeCaptionText(node: { id: string; name?: string }) {
  return node.name && node.name !== node.id ? node.name : node.id;
}

function makeNodeLabel(node: { id: string; name?: string }, isDark: boolean): NodeLabelState {
  const holder = new Container();
  holder.eventMode = 'none';
  const label = new Text({
    text: nodeCaptionText(node),
    style: new TextStyle({
      fontFamily: 'ui-sans-serif, system-ui, sans-serif',
      fontSize: 11,
      fill: isDark ? 0xe8eef4 : 0x1c2834,
      fontWeight: '500'
    }),
    resolution: Math.max(2, window.devicePixelRatio || 1)
  });
  label.anchor.set(0, 0.5);
  label.position.set(5, 0);
  const plate = new Graphics();
  plate.roundRect(2, -label.height / 2 - 1, label.width + 6, label.height + 2, 2);
  plate.fill({ color: isDark ? 0x12181e : 0xf7f8fa, alpha: 0.94 });
  const offset = new Container();
  offset.position.set(8, 0);
  offset.addChild(plate);
  offset.addChild(label);
  holder.addChild(offset);
  return { holder, label };
}

function updateNodeLabels() {
  labelsDirty = false;
  if (!labelLayer || !worldContainer || !app) return;
  const vis = props.layerVisibility;
  const pxm = SCALE * viewScale();
  const show = Boolean(vis.nodes && vis.stationLabels && pxm >= STATION_LABEL_PX_PER_M && props.graph);
  if (!show) {
    for (const state of nodeLabels.values()) state.holder.visible = false;
    return;
  }

  const graph = props.graph!;
  const isDark = themeStore.darkMode;
  const inv = 1 / viewScale();
  const scale = viewScale();
  const originX = worldContainer.position.x;
  const originY = worldContainer.position.y;
  const screenW = app.screen.width;
  const screenH = app.screen.height;
  const slots = new Set<string>();
  const selectedId = props.selectedNodeId;
  const candidates: Array<{ node: (typeof graph.nodes)[number]; nx: number; ny: number; priority: number }> = [];

  for (const node of graph.nodes) {
    const nx = node.x * SCALE;
    const ny = -node.y * SCALE;
    const selected = node.id === selectedId;
    const sx = nx * scale + originX;
    const sy = ny * scale + originY;
    if (!selected && (sx < -48 || sx > screenW + 48 || sy < -24 || sy > screenH + 24)) continue;
    const kind = stationKind(node);
    candidates.push({ node, nx, ny, priority: selected ? 0 : kind === 'way' ? 2 : 1 });
  }
  candidates.sort((a, b) => a.priority - b.priority);

  const keep = new Set<string>();
  let visible = 0;
  let created = 0;
  for (const item of candidates) {
    if (visible >= LABEL_MAX_VISIBLE) break;
    const selected = item.node.id === selectedId;
    if (!claimLabelSlot(slots, item.nx, item.ny, selected)) continue;
    let state = nodeLabels.get(item.node.id);
    if (!state) {
      if (created >= LABEL_CREATE_BUDGET) {
        labelsDirty = true;
        continue;
      }
      state = makeNodeLabel(item.node, isDark);
      nodeLabels.set(item.node.id, state);
      labelLayer.addChild(state.holder);
      created += 1;
    }
    const caption = nodeCaptionText(item.node);
    if (state.label.text !== caption) state.label.text = caption;
    state.holder.visible = true;
    state.holder.position.set(item.nx, item.ny);
    state.holder.scale.set(inv);
    keep.add(item.node.id);
    visible += 1;
  }

  for (const [id, state] of nodeLabels) {
    if (!keep.has(id)) state.holder.visible = false;
  }
}

function stationKind(node: { id: string; name?: string; type?: string }) {
  const type = node.type || '';
  if (type === 'Charge' || type === 'ChargePoint') return 'charge';
  if (type === 'Pick' || type === 'Drop' || type === 'JobPoint') return 'job';
  if (type === 'Park' || type === 'Rest' || type === 'PullOverPoint' || type === 'StandbyPoint') return 'park';
  return 'way';
}

function claimLabelSlot(slots: Set<string>, x: number, y: number, force: boolean) {
  const col = Math.round(x / ink(78));
  const row = Math.round(y / ink(16));
  const key = `${col}:${row}`;
  if (!force && slots.has(key)) return false;
  slots.add(key);
  return true;
}

function drawStationMark(g: Graphics, kind: string, selected: boolean, isDark: boolean) {
  const stroke = isDark ? 0xe7eef5 : 0xffffff;
  const radius = ink(kind === 'way' ? 2.2 : 4.5);
  if (selected) {
    g.circle(0, 0, radius + ink(3.5));
    g.stroke({ width: ink(1.25), color: 0xb45309, alpha: 1 });
  }

  if (kind === 'job') {
    const side = radius * 1.7;
    g.rect(-side / 2, -side / 2, side, side);
  } else if (kind === 'park') {
    g.poly([
      { x: 0, y: -radius },
      { x: radius, y: 0 },
      { x: 0, y: radius },
      { x: -radius, y: 0 }
    ]);
  } else {
    g.circle(0, 0, radius);
  }

  const fill =
    kind === 'charge' ? 0xb45309 : kind === 'job' ? 0x1d4e89 : kind === 'park' ? 0x0f766e : kind === 'mark' || kind === 'named' ? 0x44403c : 0x64748b;
  g.fill({ color: fill, alpha: 1 });
  if (kind !== 'way') {
    g.stroke({ width: ink(1), color: stroke, alpha: 0.9 });
  }
}

function drawArrowHead(g: Graphics, x: number, y: number, angle: number, color: number) {
  const size = ink(5);
  g.poly([
    { x, y },
    { x: x - size * Math.cos(angle - Math.PI / 7), y: y - size * Math.sin(angle - Math.PI / 7) },
    { x: x - size * 0.72 * Math.cos(angle), y: y - size * 0.72 * Math.sin(angle) },
    { x: x - size * Math.cos(angle + Math.PI / 7), y: y - size * Math.sin(angle + Math.PI / 7) }
  ]);
  g.fill({ color, alpha: 0.95 });
}

function syncVehicles(vehicles: Fms.VehicleDto[]) {
  if (!vehicleLayer) return;

  const currentIds = new Set(vehicles.map(v => v.id));

  for (const [id, state] of vehicleSprites.entries()) {
    if (!currentIds.has(id)) {
      vehicleLayer.removeChild(state.container);
      state.container.destroy({ children: true });
      vehicleSprites.delete(id);
    }
  }

  for (const v of vehicles) {
    let state = vehicleSprites.get(v.id);
    if (!state) {
      state = createVehicleSprite(v);
      vehicleSprites.set(v.id, state);
      vehicleLayer.addChild(state.container);
    }

    state.targetPose = {
      x: v.pose.x * SCALE,
      y: -v.pose.y * SCALE,
      theta: -v.pose.theta
    };

    updateVehicleVisuals(state, v);
  }
}

function createVehicleSprite(v: Fms.VehicleDto): VehicleRenderState {
  const container = new Container();
  container.eventMode = 'static';
  container.cursor = 'pointer';
  container.on('pointerdown', e => {
    e.stopPropagation();
    emit('select-vehicle', v.id);
  });

  const laserScan = new Graphics();
  const graphics = new Graphics();
  const statusBadge = new Graphics();

  const label = new Text({
    text: v.name && v.name !== v.id ? v.name : v.id,
    style: new TextStyle({
      fontFamily: 'ui-sans-serif, system-ui, sans-serif',
      fontSize: 12,
      fill: themeStore.darkMode ? 0xe7eef5 : 0x1f2937,
      fontWeight: '500'
    }),
    resolution: Math.min(2, window.devicePixelRatio || 1)
  });
  label.anchor.set(0.5, 0.5);

  container.addChild(laserScan);
  container.addChild(graphics);
  container.addChild(statusBadge);
  container.addChild(label);

  const initPose = {
    x: v.pose.x * SCALE,
    y: -v.pose.y * SCALE,
    theta: -v.pose.theta
  };

  container.position.set(initPose.x, initPose.y);
  container.rotation = initPose.theta;
  label.rotation = -initPose.theta;

  return {
    currentPose: { ...initPose },
    targetPose: { ...initPose },
    container,
    graphics,
    label,
    laserScan,
    statusBadge
  };
}

function updateVehicleVisuals(state: VehicleRenderState, v: Fms.VehicleDto) {
  const g = state.graphics;
  g.clear();

  const isSelected = props.selectedVehicleId === v.id;
  const length = 0.96 * SCALE;
  const width = 0.64 * SCALE;
  const nose = 0.16 * SCALE;
  const pad = 0.08 * SCALE;

  if (isSelected) {
    g.roundRect(-length / 2 - pad, -width / 2 - pad, length + pad * 2, width + pad * 2, 0.05 * SCALE);
    g.stroke({ width: 0.03 * SCALE, color: 0xb45309, alpha: 1 });
  }

  let chassisColor = 0x1d4e89;
  switch (v.status) {
    case 'Idle':
      chassisColor = 0x0f766e;
      break;
    case 'WaitingLock':
      chassisColor = 0xb45309;
      break;
    case 'Avoiding':
      chassisColor = 0x9f1239;
      break;
    case 'Error':
      chassisColor = 0xb91c1c;
      break;
    case 'Charging':
      chassisColor = 0x6d28d9;
      break;
    case 'Paused':
      chassisColor = 0x475569;
      break;
  }

  g.roundRect(-length / 2, -width / 2, length - nose, width, 0.05 * SCALE);
  g.fill({ color: chassisColor, alpha: 1 });
  g.poly([
    { x: length / 2 - nose, y: -width / 2 },
    { x: length / 2, y: 0 },
    { x: length / 2 - nose, y: width / 2 }
  ]);
  g.fill({ color: chassisColor, alpha: 1 });

  const wheelW = 0.07 * SCALE;
  const wheelL = 0.22 * SCALE;
  const wheelX = -0.08 * SCALE;
  g.rect(wheelX, -width / 2 - wheelW * 0.35, wheelL, wheelW);
  g.rect(wheelX, width / 2 - wheelW * 0.65, wheelL, wheelW);
  g.fill({ color: 0x1c1917, alpha: 1 });

  if (v.holdingPayload) {
    g.roundRect(-0.16 * SCALE, -0.16 * SCALE, 0.32 * SCALE, 0.32 * SCALE, 0.02 * SCALE);
    g.fill({ color: 0xd6d3d1, alpha: 1 });
    g.stroke({ width: 0.025 * SCALE, color: 0x44403c, alpha: 0.8 });
  }

  const lScan = state.laserScan;
  lScan.clear();
  if (v.status === 'Running' || v.status === 'Avoiding') {
    lScan.moveTo(length / 2, 0);
    lScan.arc(length / 2, 0, 1.15 * SCALE, -Math.PI / 5, Math.PI / 5);
    lScan.closePath();
    lScan.fill({ color: chassisColor, alpha: 0.08 });
  }

  state.statusBadge.clear();
  state.label.text = Number.isFinite(v.batteryPercent)
    ? `${v.name && v.name !== v.id ? v.name : v.id}  ${Math.round(v.batteryPercent)}%`
    : `${v.name && v.name !== v.id ? v.name : v.id}`;
}

const vehicleLabelSlots = new Set<string>();

let overlayPhase = 0;

/**
 * 沿折线画流动虚线：offset 随时间推进，视觉上沿行驶方向流动。
 * 按累计里程逐段取点，循环次数有上界，不会因为浮点误差卡死。
 */
function dashPolyline(g: Graphics, pts: { x: number; y: number }[], dash: number, gap: number, offset: number) {
  const n = pts.length;
  const period = dash + gap;
  if (n < 2 || !Number.isFinite(period) || !(dash > 0) || !(period > 1e-9)) return;

  const cum = new Array<number>(n);
  cum[0] = 0;
  for (let i = 1; i < n; i++) cum[i] = cum[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
  const total = cum[n - 1];
  if (!Number.isFinite(total) || total < 1e-6) return;

  // 极端放大时虚线数量会很大，退化成实线，保证每帧开销有上界
  if (total / period > 900) {
    g.moveTo(pts[0].x, pts[0].y);
    for (let i = 1; i < n; i++) g.lineTo(pts[i].x, pts[i].y);
    return;
  }

  let seg = 0;
  const locate = (d: number) => {
    while (seg < n - 2 && cum[seg + 1] < d) seg++;
    const span = cum[seg + 1] - cum[seg];
    const t = span > 1e-9 ? Math.min(1, Math.max(0, (d - cum[seg]) / span)) : 0;
    return { x: pts[seg].x + (pts[seg + 1].x - pts[seg].x) * t, y: pts[seg].y + (pts[seg + 1].y - pts[seg].y) * t };
  };

  const firstIndex = Math.ceil((offset - dash) / period);
  const maxDashes = Math.ceil(total / period) + 2;
  for (let k = 0; k < maxDashes; k++) {
    const start = (firstIndex + k) * period - offset;
    if (start >= total) break;
    const a = Math.max(start, 0);
    const b = Math.min(start + dash, total);
    if (b - a < 1e-6) continue;

    const head = locate(a);
    g.moveTo(head.x, head.y);
    for (let i = seg + 1; i < n - 1 && cum[i] < b; i++) g.lineTo(pts[i].x, pts[i].y);
    const tail = locate(b);
    g.lineTo(tail.x, tail.y);
  }
}

/** 在途任务：车辆剩余路线（流动虚线）+ 目的地脉冲圈。每帧重画，起点跟随车辆当前渲染位置。 */
function drawTaskOverlay(deltaTime: number) {
  if (!taskLayer) return;
  taskLayer.clear();
  const orders = props.orders || [];
  if (orders.length === 0) return;

  overlayPhase = (overlayPhase + deltaTime / 90) % 1;
  const dark = themeStore.darkMode;
  const accent = dark ? 0x5aa7ff : 0x1d5fd1;

  for (const order of orders) {
    const route = order.route;
    if (!route || route.length < 2) continue;

    const pts = route.map(([x, y]) => ({ x: x * SCALE, y: -y * SCALE }));
    const sprite = order.assignedVehicleId ? vehicleSprites.get(order.assignedVehicleId) : undefined;
    if (sprite) pts[0] = { x: sprite.currentPose.x, y: sprite.currentPose.y };

    const selected = !!props.selectedVehicleId && props.selectedVehicleId === order.assignedVehicleId;
    const width = ink(selected ? 2.4 : 1.6);
    const alpha = selected ? 0.95 : 0.62;
    const dash = ink(selected ? 8 : 6);
    const gap = ink(selected ? 5 : 6);

    dashPolyline(taskLayer, pts, dash, gap, overlayPhase * (dash + gap));
    taskLayer.stroke({ width, color: accent, alpha, cap: 'round', join: 'round' });

    const end = pts[pts.length - 1];
    taskLayer.circle(end.x, end.y, ink(selected ? 4 : 3));
    taskLayer.fill({ color: accent, alpha: 0.95 });
    taskLayer.circle(end.x, end.y, ink(5 + overlayPhase * 11));
    taskLayer.stroke({ width: ink(1.2), color: accent, alpha: (1 - overlayPhase) * 0.6 });
  }
}

function updateVehicleAnimation(deltaTime: number) {
  const lerpFactor = Math.min(1.0, deltaTime * 0.2);
  const pxm = SCALE * viewScale();
  const boost = Math.max(1, 12 / Math.max(0.96 * pxm, 0.001));
  const showLabels = pxm >= STATION_LABEL_PX_PER_M;
  vehicleLabelSlots.clear();

  for (const state of vehicleSprites.values()) {
    const cur = state.currentPose;
    const tgt = state.targetPose;

    cur.x += (tgt.x - cur.x) * lerpFactor;
    cur.y += (tgt.y - cur.y) * lerpFactor;

    let angleDiff = tgt.theta - cur.theta;
    while (angleDiff > Math.PI) angleDiff -= 2 * Math.PI;
    while (angleDiff < -Math.PI) angleDiff += 2 * Math.PI;
    cur.theta += angleDiff * lerpFactor;

    state.container.position.set(cur.x, cur.y);
    state.container.rotation = cur.theta;
    state.graphics.scale.set(boost);
    state.laserScan.scale.set(boost);
    state.statusBadge.scale.set(boost);
    state.laserScan.visible = pxm >= STATION_LABEL_PX_PER_M;
    state.label.rotation = -cur.theta;
    state.label.scale.set(1 / viewScale());
    state.label.position.set(0, -ink(16));
    const slot = `${Math.round(cur.x / ink(140))}:${Math.round(cur.y / ink(20))}`;
    const free = !vehicleLabelSlots.has(slot);
    state.label.visible = showLabels && free;
    if (state.label.visible) vehicleLabelSlots.add(slot);
  }
}

function zoomAroundCenter(factor: number) {
  if (!worldContainer || !app) return;
  userMoved = true;
  const oldScale = worldContainer.scale.x;
  const newScale = Math.max(0.02, Math.min(10, oldScale * factor));
  const cx = app.screen.width / 2;
  const cy = app.screen.height / 2;
  const worldX = (cx - worldContainer.position.x) / oldScale;
  const worldY = (cy - worldContainer.position.y) / oldScale;
  worldContainer.scale.set(newScale);
  worldContainer.position.set(cx - worldX * newScale, cy - worldY * newScale);
  zoomLevel.value = newScale;
  renderGrid();
  labelsDirty = true;
  updateNodeLabels();
  scheduleStaticRebuild();
}

function zoomIn() {
  zoomAroundCenter(1.25);
}

function zoomOut() {
  zoomAroundCenter(0.8);
}

function resetView() {
  if (!worldContainer || !app) return;
  userMoved = true;
  worldContainer.scale.set(1.0);
  worldContainer.position.set(app.screen.width / 2, app.screen.height / 2);
  zoomLevel.value = 1.0;
  renderGrid();
  labelsDirty = true;
  scheduleStaticRebuild();
}

function fitToScreen() {
  if (!worldContainer || !app || !props.graph || props.graph.nodes.length === 0) return;
  if (app.screen.width < 32 || app.screen.height < 32) return;

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  const include = (x: number, y: number) => {
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
  };
  for (const n of props.graph.nodes) include(n.x * SCALE, -n.y * SCALE);
  const nodeSpanX = maxX - minX;
  const nodeSpanY = maxY - minY;
  if (props.raster && props.raster.widthMeters > 0 && props.raster.heightMeters > 0) {
    const rasterW = props.raster.widthMeters * SCALE;
    const rasterH = props.raster.heightMeters * SCALE;
    if (rasterW < nodeSpanX * 1.35 && rasterH < nodeSpanY * 1.35) {
      const x0 = props.raster.originX * SCALE;
      const y0 = -props.raster.topY * SCALE;
      include(x0, y0);
      include(x0 + rasterW, y0 + rasterH);
    }
  }

  const margin = 36;
  const width = maxX - minX;
  const height = maxY - minY;
  if (width <= 0 || height <= 0) return;

  const scaleX = (app.screen.width - margin * 2) / width;
  const scaleY = (app.screen.height - margin * 2) / height;
  const fitScale = Math.max(0.02, Math.min(2.5, Math.min(scaleX, scaleY)));

  worldContainer.scale.set(fitScale);
  zoomLevel.value = fitScale;
  fittedScale.value = fitScale;

  const centerX = (minX + maxX) / 2;
  const centerY = (minY + maxY) / 2;
  worldContainer.position.set(app.screen.width / 2 - centerX * fitScale, app.screen.height / 2 - centerY * fitScale);
  userMoved = false;

  renderGrid();
  renderStaticMap();
  drawnLod = zoomLod();
  labelsDirty = true;
}

/** 把镜头移到车辆身上；缩放不足时放大到能看清车身与站点标签。 */
function focusVehicle(id: string) {
  const state = vehicleSprites.get(id);
  if (!state || !worldContainer || !app) return;
  userMoved = true;
  const scale = Math.max(worldContainer.scale.x, 16 / SCALE);
  worldContainer.scale.set(scale);
  worldContainer.position.set(app.screen.width / 2 - state.currentPose.x * scale, app.screen.height / 2 - state.currentPose.y * scale);
  zoomLevel.value = scale;
  renderGrid();
  labelsDirty = true;
  scheduleStaticRebuild();
}

/** 镜头平滑跟随车辆；传 null 取消。用户手动拖动画面时自动取消。 */
function followVehicle(id: string | null) {
  if (!id) {
    if (followId !== null) {
      followId = null;
      emit('follow-change', null);
    }
    return;
  }
  const state = vehicleSprites.get(id);
  if (!state || !worldContainer || !app) return;
  userMoved = true;
  if (worldContainer.scale.x < 16 / SCALE) {
    worldContainer.scale.set(16 / SCALE);
    zoomLevel.value = 16 / SCALE;
    labelsDirty = true;
    scheduleStaticRebuild();
  }
  followId = id;
  emit('follow-change', id);
}

function updateFollowCamera() {
  if (!followId || !worldContainer || !app || isDragging) return;
  const state = vehicleSprites.get(followId);
  if (!state) return;
  const scale = worldContainer.scale.x;
  const targetX = app.screen.width / 2 - state.currentPose.x * scale;
  const targetY = app.screen.height / 2 - state.currentPose.y * scale;
  const dx = (targetX - worldContainer.position.x) * 0.18;
  const dy = (targetY - worldContainer.position.y) * 0.18;
  if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) return;
  worldContainer.position.set(worldContainer.position.x + dx, worldContainer.position.y + dy);
  renderGrid();
  labelsDirty = true;
}

defineExpose({
  followVehicle,
  focusVehicle,
  fitToScreen,
  resetView,
  zoomIn,
  zoomOut
});
</script>

<template>
  <div class="relative h-full w-full select-none overflow-hidden bg-[#f7f8fa] dark:bg-[#101418]">
    <!-- PixiJS Canvas Container -->
    <div ref="canvasContainerRef" class="h-full w-full"></div>

    <!-- Floating Viewport Controls -->
    <div
      class="absolute z-10 flex flex-col gap-0.5 rounded-md border border-slate-200 bg-white/95 p-1 text-12px text-slate-700 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95 dark:text-slate-200"
      :style="{ right: `${hud.right}px`, bottom: `${hud.bottom}px` }"
    >
      <button
        title="放大"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 dark:text-slate-200 transition-all hover:bg-slate-100 dark:hover:bg-white/10 active:scale-90"
        @click="zoomIn"
      >
        <div class="i-carbon-zoom-in text-16px"></div>
      </button>
      <button
        title="缩小"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 dark:text-slate-200 transition-all hover:bg-slate-100 dark:hover:bg-white/10 active:scale-90"
        @click="zoomOut"
      >
        <div class="i-carbon-zoom-out text-16px"></div>
      </button>
      <div class="my-0.5 h-[1px] bg-slate-200/80 dark:bg-slate-700/50"></div>
      <button
        title="适应视图"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 dark:text-slate-200 transition-all hover:bg-slate-100 dark:hover:bg-white/10 active:scale-90"
        @click="fitToScreen"
      >
        <div class="i-carbon-fit-to-screen text-16px"></div>
      </button>
      <button
        title="重置视角 (1:1)"
        class="h-8 w-8 flex-center rounded-lg text-slate-700 dark:text-slate-200 transition-all hover:bg-slate-100 dark:hover:bg-white/10 active:scale-90"
        @click="resetView"
      >
        <div class="i-carbon-restart text-16px"></div>
      </button>
    </div>

    <!-- Floating Coordinates & Zoom Level Bar -->
    <div
      class="absolute z-10 flex items-center gap-3 rounded-md border border-slate-200 bg-white/95 px-2.5 py-1.5 font-mono text-[11px] tabular-nums text-slate-600 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95 dark:text-slate-300"
      :style="{ left: `${hud.left}px`, bottom: `${hud.bottom}px` }"
    >
      <span class="inline-block h-px bg-slate-700 align-middle dark:bg-slate-200" :style="{ width: `${scaleBar.px}px` }"></span>
      <span>{{ scaleBar.label }}</span>
      <span>X {{ cursorWorldPos.x.toFixed(2) }}</span>
      <span>Y {{ cursorWorldPos.y.toFixed(2) }}</span>
      <span class="text-slate-400">{{ displayZoom }}%</span>
    </div>
  </div>
</template>
