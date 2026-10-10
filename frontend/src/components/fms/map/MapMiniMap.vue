<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { SCALE } from './constants';
import { sampleBezier } from './geometry';
import { cssColor, getPalette, stationColor, vehicleColor } from './palette';
import { stationKindOf } from './stations';

const props = withDefaults(
  defineProps<{
    graph?: Fms.MapGraphDto | null;
    vehicles?: Fms.VehicleDto[];
    dark?: boolean;
    /** 视口四角（局部像素），[x0,y0,x1,y1,x2,y2,x3,y3] */
    quad?: number[] | null;
    selectedVehicleId?: string | null;
    width?: number;
    height?: number;
  }>(),
  { graph: null, vehicles: () => [], dark: false, quad: null, selectedVehicleId: null, width: 208, height: 136 }
);

const emit = defineEmits<{
  (e: 'jump', payload: { x: number; y: number; phase: 'start' | 'move' | 'end' }): void;
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
const collapsed = ref(false);

let base: HTMLCanvasElement | null = null;
let fit = { s: 1, ox: 0, oy: 0 };
let raf = 0;
let dragging = false;

function dpr() {
  return Math.min(3, Math.max(2, window.devicePixelRatio || 1));
}

function buildBase() {
  const graph = props.graph;
  if (!graph || !graph.nodes.length) {
    base = null;
    return;
  }
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const n of graph.nodes) {
    const x = n.x * SCALE;
    const y = -n.y * SCALE;
    if (x < minX) minX = x;
    if (y < minY) minY = y;
    if (x > maxX) maxX = x;
    if (y > maxY) maxY = y;
  }
  const pad = 10;
  const bw = Math.max(maxX - minX, 1);
  const bh = Math.max(maxY - minY, 1);
  const s = Math.min((props.width - pad * 2) / bw, (props.height - pad * 2) / bh);
  fit = { s, ox: (props.width - bw * s) / 2 - minX * s, oy: (props.height - bh * s) / 2 - minY * s };

  const ratio = dpr();
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(props.width * ratio);
  canvas.height = Math.round(props.height * ratio);
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  ctx.scale(ratio, ratio);
  const palette = getPalette(props.dark);

  const byId = new Map(graph.nodes.map(n => [n.id, n]));
  ctx.strokeStyle = cssColor(palette.laneFaint);
  ctx.globalAlpha = props.dark ? 0.75 : 0.7;
  ctx.lineWidth = 0.8;
  ctx.lineJoin = 'round';
  ctx.beginPath();
  const seen = new Set<string>();
  for (const path of graph.paths) {
    const a = byId.get(path.startNodeId);
    const b = byId.get(path.endNodeId);
    if (!a || !b) continue;
    const key = path.startNodeId < path.endNodeId ? `${path.startNodeId}|${path.endNodeId}` : `${path.endNodeId}|${path.startNodeId}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const x0 = a.x * SCALE * fit.s + fit.ox;
    const y0 = -a.y * SCALE * fit.s + fit.oy;
    const x3 = b.x * SCALE * fit.s + fit.ox;
    const y3 = -b.y * SCALE * fit.s + fit.oy;
    ctx.moveTo(x0, y0);
    if (path.pathType === 'CubicBezier' && path.controlPoints?.length >= 2) {
      const pts = sampleBezier(
        x0,
        y0,
        path.controlPoints[0].x * SCALE * fit.s + fit.ox,
        -path.controlPoints[0].y * SCALE * fit.s + fit.oy,
        path.controlPoints[1].x * SCALE * fit.s + fit.ox,
        -path.controlPoints[1].y * SCALE * fit.s + fit.oy,
        x3,
        y3,
        8
      );
      for (let i = 1; i < pts.length; i += 1) ctx.lineTo(pts[i].x, pts[i].y);
    } else {
      ctx.lineTo(x3, y3);
    }
  }
  ctx.stroke();

  ctx.globalAlpha = 1;
  for (const n of graph.nodes) {
    const kind = stationKindOf(n.type);
    if (kind === 'landmark') continue;
    ctx.fillStyle = cssColor(stationColor(kind, props.dark));
    ctx.beginPath();
    ctx.arc(n.x * SCALE * fit.s + fit.ox, -n.y * SCALE * fit.s + fit.oy, 1.7, 0, Math.PI * 2);
    ctx.fill();
  }
  base = canvas;
}

function draw() {
  raf = 0;
  const canvas = canvasRef.value;
  if (!canvas) return;
  const ratio = dpr();
  if (canvas.width !== Math.round(props.width * ratio)) {
    canvas.width = Math.round(props.width * ratio);
    canvas.height = Math.round(props.height * ratio);
  }
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (base) ctx.drawImage(base, 0, 0);
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  const palette = getPalette(props.dark);

  for (const v of props.vehicles) {
    const x = v.pose.x * SCALE * fit.s + fit.ox;
    const y = -v.pose.y * SCALE * fit.s + fit.oy;
    const selected = props.selectedVehicleId === v.id;
    ctx.fillStyle = cssColor(vehicleColor(v.status, props.dark));
    ctx.beginPath();
    ctx.arc(x, y, selected ? 3.6 : 2.3, 0, Math.PI * 2);
    ctx.fill();
    if (selected) {
      ctx.strokeStyle = cssColor(palette.select);
      ctx.lineWidth = 1.2;
      ctx.stroke();
    }
  }

  const quad = props.quad;
  if (quad && quad.length >= 8) {
    ctx.beginPath();
    for (let i = 0; i < 4; i += 1) {
      const x = quad[i * 2] * fit.s + fit.ox;
      const y = quad[i * 2 + 1] * fit.s + fit.oy;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.closePath();
    ctx.fillStyle = props.dark ? 'rgba(90,167,255,0.12)' : 'rgba(29,95,209,0.09)';
    ctx.fill();
    ctx.strokeStyle = cssColor(palette.accent);
    ctx.lineWidth = 1.3;
    ctx.stroke();
  }
}

function schedule() {
  if (raf) return;
  raf = requestAnimationFrame(draw);
}

watch(
  () => [props.graph, props.graph?.nodes.length, props.graph?.paths.length, props.dark, props.width, props.height] as const,
  () => {
    buildBase();
    schedule();
  }
);
watch(() => props.vehicles, schedule, { deep: true });
watch(() => [props.quad, props.selectedVehicleId], schedule);
watch(collapsed, value => {
  if (!value) {
    buildBase();
    requestAnimationFrame(schedule);
  }
});

onMounted(() => {
  buildBase();
  schedule();
});

onBeforeUnmount(() => {
  if (raf) cancelAnimationFrame(raf);
});

function toLocal(event: PointerEvent) {
  const canvas = canvasRef.value;
  if (!canvas) return null;
  const rect = canvas.getBoundingClientRect();
  return {
    x: (event.clientX - rect.left - fit.ox) / fit.s,
    y: (event.clientY - rect.top - fit.oy) / fit.s
  };
}

function onPointerDown(event: PointerEvent) {
  if (event.button !== 0) return;
  const p = toLocal(event);
  if (!p) return;
  dragging = true;
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  emit('jump', { ...p, phase: 'start' });
}

function onPointerMove(event: PointerEvent) {
  if (!dragging) return;
  const p = toLocal(event);
  if (p) emit('jump', { ...p, phase: 'move' });
}

function onPointerUp(event: PointerEvent) {
  if (!dragging) return;
  dragging = false;
  const p = toLocal(event);
  if (p) emit('jump', { ...p, phase: 'end' });
}
</script>

<template>
  <div
    class="relative overflow-hidden rounded-md border border-slate-200 bg-white/95 shadow-sm dark:border-slate-700 dark:bg-[#151b22]/95"
    :style="{ width: collapsed ? '88px' : `${width}px` }"
    @pointerdown.stop
    @wheel.stop
    @contextmenu.prevent.stop
  >
    <div class="flex h-6 items-center justify-between px-2 text-[10px] tracking-wide text-slate-400">
      <span>鹰眼</span>
      <button
        type="button"
        class="flex h-4 w-4 items-center justify-center rounded text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/10 dark:hover:text-slate-200"
        :title="collapsed ? '展开鹰眼' : '收起鹰眼'"
        @click="collapsed = !collapsed"
      >
        <div :class="collapsed ? 'i-carbon-chevron-up' : 'i-carbon-chevron-down'" class="text-12px" />
      </button>
    </div>
    <canvas
      v-show="!collapsed"
      ref="canvasRef"
      class="block cursor-crosshair touch-none"
      :style="{ width: `${width}px`, height: `${height}px` }"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerUp"
    />
  </div>
</template>
