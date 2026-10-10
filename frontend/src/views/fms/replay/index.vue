<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import MapCanvas from '@/components/fms/MapCanvas.vue';
import { fmsApi } from '@/service/api/fms';
import { useMapStore } from '@/store/modules/map';
import { orderStatusLabel, panelClass, stageClass } from '@/utils/fms-display';

interface ReplayVehicle {
  id: string;
  x: number;
  y: number;
  th: number;
  st: string;
}

interface ReplaySample {
  t: number;
  vehicles: ReplayVehicle[];
}

interface ReplayLog {
  simMs: number;
  line: string;
}

interface ReplayScene {
  id: string;
  name: string;
  mapName: string;
  traceId: string;
  vehicleId: string;
  status: string;
  startSimMs: number;
  endSimMs: number;
  sampleCount: number;
  createdAt: string;
  frames?: ReplaySample[];
  logs?: ReplayLog[];
}

interface SceneSummary {
  id: string;
  name: string;
  mapName: string;
  traceId: string;
  status: string;
  sampleCount: number;
  createdAt: string;
}

const route = useRoute();
const router = useRouter();
const mapStore = useMapStore();
const scenes = ref<SceneSummary[]>([]);
const scene = ref<ReplayScene | null>(null);
const index = ref(0);
const playing = ref(false);
const speed = ref(1);
let timer: number | undefined;

const samples = computed(() => scene.value?.frames || []);
const logs = computed(() => scene.value?.logs || []);
const current = computed(() => samples.value[index.value] || null);
const progress = computed(() => (samples.value.length <= 1 ? 0 : (index.value / (samples.value.length - 1)) * 100));
const activeLog = computed(() => {
  const time = current.value?.t ?? scene.value?.startSimMs ?? 0;
  let hit = -1;
  logs.value.forEach((line, lineIndex) => {
    if (line.simMs <= time) hit = lineIndex;
  });
  return hit;
});

const playbackVehicles = computed<Fms.VehicleDto[]>(() =>
  (current.value?.vehicles || []).map(vehicle => ({
    id: vehicle.id,
    name: vehicle.id,
    status: (vehicle.st || 'Idle') as Fms.VehicleStatus,
    pose: { x: vehicle.x, y: vehicle.y, theta: vehicle.th },
    currentSpeed: 0,
    batteryPercent: Number.NaN,
    batteryVoltage: 48,
    driveType: 'Differential',
    holdingPayload: false
  }))
);

function formatOffset(ms: number) {
  const start = scene.value?.startSimMs || ms;
  const delta = Math.max(0, ms - start);
  const total = Math.floor(delta / 1000);
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

function stop() {
  playing.value = false;
  if (timer) window.clearInterval(timer);
  timer = undefined;
}

function play() {
  if (samples.value.length < 2) return;
  stop();
  playing.value = true;
  const gap = Math.max(40, Math.round(220 / speed.value));
  timer = window.setInterval(() => {
    if (index.value >= samples.value.length - 1) {
      stop();
      return;
    }
    index.value += 1;
  }, gap);
}

function toggle() {
  if (playing.value) stop();
  else play();
}

function seek(next: number) {
  index.value = Math.min(samples.value.length - 1, Math.max(0, next));
}

function seekLog(line: ReplayLog) {
  const target = samples.value.findIndex(sample => sample.t >= line.simMs);
  seek(target < 0 ? samples.value.length - 1 : target);
}

async function loadList() {
  const res = await fmsApi.listReplayScenes();
  scenes.value = res.items || [];
}

async function openScene(id: string) {
  stop();
  index.value = 0;
  const res = await fmsApi.getReplayScene(id);
  scene.value = res.scene;
  router.replace({ path: '/fms/replay', query: { id: res.scene.id } });
}

onMounted(async () => {
  if (!mapStore.currentGraph) await mapStore.loadCurrentMap();
  await loadList();
  const id = (route.query.id || route.query.trace) as string | undefined;
  if (id) await openScene(id);
  else if (scenes.value[0]) await openScene(scenes.value[0].id);
});

watch(speed, () => {
  if (playing.value) play();
});

onUnmounted(stop);
</script>

<template>
  <div :class="stageClass" class="h-[calc(100vh-108px)]">
    <MapCanvas
      class="absolute inset-0"
      :graph="mapStore.currentGraph"
      :vehicles="playbackVehicles"
      :point-cloud="mapStore.pointCloud"
      :raster="mapStore.raster"
      :selected-vehicle-id="scene?.vehicleId || null"
      :layer-visibility="mapStore.layerVisibility"
      :hud-inset="{ left: 274, right: 344, bottom: 132 }"
    />

    <aside :class="panelClass" class="absolute bottom-24 left-4 top-4 z-10 w-[250px] overflow-auto p-3">
      <div class="text-[10px] tracking-[0.18em] text-sky-600 dark:text-cyan-300">监控回放</div>
      <div class="mt-1 text-sm font-semibold">已归档场景</div>
      <div class="mt-1 text-[11px] text-slate-400">任务结束后自动留下地图画面和工步日志</div>
      <button
        v-for="item in scenes"
        :key="item.id"
        class="mt-2 w-full rounded-xl border px-2.5 py-2 text-left"
        :class="scene?.id === item.id ? 'border-sky-500 bg-sky-500/10' : 'border-slate-200 dark:border-white/10'"
        @click="openScene(item.id)"
      >
        <div class="truncate text-[12px] font-semibold">{{ item.name }}</div>
        <div class="mt-1 truncate text-[10px] text-slate-500 dark:text-slate-400">{{ orderStatusLabel(item.status) }} · {{ item.sampleCount }} 帧 · {{ item.mapName }}</div>
      </button>
      <div v-if="scenes.length === 0" class="py-6 text-center text-[11px] text-slate-500">还没有可回看的场景。在仿真里跑完一段任务，或点保存当前画面。</div>
    </aside>

    <aside :class="panelClass" class="absolute bottom-24 right-4 top-4 z-10 flex w-[320px] flex-col">
      <div class="flex items-center justify-between border-b border-slate-200 px-3 py-2 text-[11px] dark:border-white/10">
        <span>同步日志</span>
        <RouterLink to="/logs/runtime" class="text-cyan-300">运行日志</RouterLink>
      </div>
      <div class="min-h-0 flex-1 overflow-auto px-2 py-2 font-mono text-[11px] leading-5">
        <button
          v-for="(line, lineIndex) in logs"
          :key="lineIndex"
          class="mb-1 block w-full rounded-lg px-2 py-1 text-left"
          :class="lineIndex === activeLog ? 'bg-sky-500/15 text-sky-800 dark:text-cyan-100' : 'text-slate-500 dark:text-slate-400'"
          @click="seekLog(line)"
        >
          {{ line.line }}
        </button>
        <div v-if="logs.length === 0" class="px-2 py-4 text-slate-500">这段回放没有工步日志。</div>
      </div>
    </aside>

    <div :class="panelClass" class="absolute inset-x-4 bottom-4 z-10 px-4 py-3">
      <div class="mb-2 flex items-center justify-between gap-3 text-[11px] text-slate-500 dark:text-slate-400">
        <span class="truncate">{{ scene?.name || '未选择片段' }} · {{ scene?.mapName || mapStore.currentGraph?.name }}</span>
        <span class="font-mono">{{ formatOffset(current?.t || scene?.startSimMs || 0) }} / {{ formatOffset(scene?.endSimMs || 0) }}</span>
      </div>
      <input
        class="w-full accent-cyan-400"
        type="range"
        min="0"
        :max="Math.max(0, samples.length - 1)"
        :value="index"
        @input="seek(Number(($event.target as HTMLInputElement).value))"
      />
      <div class="mt-2 h-1 overflow-hidden rounded bg-slate-200 dark:bg-white/10">
        <div class="h-full bg-sky-500" :style="{ width: `${progress}%` }" />
      </div>
      <div class="mt-2 flex items-center gap-2">
        <button class="rounded-lg bg-sky-600 px-3 py-1 text-xs font-bold text-white" @click="toggle">{{ playing ? '暂停' : '播放' }}</button>
        <button v-for="item in [1, 2, 4]" :key="item" class="rounded-lg border border-slate-200 px-2 py-1 text-[11px] dark:border-white/10" :class="speed === item ? 'bg-sky-600 text-white' : 'text-slate-600 dark:text-slate-300'" @click="speed = item">{{ item }}x</button>
        <span v-if="samples.length === 0" class="text-[11px] text-amber-600 dark:text-amber-200">这段没有车位帧，只能翻日志。</span>
      </div>
    </div>
  </div>
</template>
