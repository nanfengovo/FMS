<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import MapCanvas from '@/components/fms/MapCanvas.vue';
import { useAuth } from '@/hooks/business/auth';
import { fmsApi } from '@/service/api/fms';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';
import { orderStatusLabel, panelClass, stageClass, vehicleStatusLabel } from '@/utils/fms-display';

interface TianfengBlock {
  id: string;
  actionType: string;
  name: string;
  category: 'nav' | 'action' | 'control';
  nodeId: string;
  dwellSec: number;
}

interface LiveItem {
  instanceId: string;
  name: string;
  status: string;
  vehicleId?: string;
  currentStep?: string;
  targetNodeId?: string;
  startedAt?: string;
}

const route = useRoute();
const router = useRouter();
const { checkPermission } = useAuth();
const mapStore = useMapStore();
const simStore = useSimulationStore();

const palette = [
  { actionType: 'MOVE_TO_POINT', name: '导航到站点', category: 'nav' as const, dwellSec: 1, tone: '#0284c7' },
  { actionType: 'LIFT_UP', name: '顶升取货', category: 'action' as const, dwellSec: 3, tone: '#d97706' },
  { actionType: 'PUT_DOWN', name: '下降放货', category: 'action' as const, dwellSec: 3, tone: '#059669' },
  { actionType: 'WAIT_TIME', name: '原地等待', category: 'control' as const, dwellSec: 4, tone: '#475569' },
  { actionType: 'AUTO_CHARGE', name: '去充电', category: 'control' as const, dwellSec: 8, tone: '#7c3aed' }
];

const blocks = ref<TianfengBlock[]>([]);
const selectedBlockId = ref<string | null>(null);
const vehicleId = ref('');
const traceId = ref('');
const execution = ref<any>(null);
const liveItems = ref<LiveItem[]>([]);
const running = ref(false);
let poller: number | undefined;

const selectedBlock = computed(() => blocks.value.find(block => block.id === selectedBlockId.value) || null);
const traceTarget = computed(() => execution.value?.targetNodeId || selectedBlock.value?.nodeId || null);
const traceVehicle = computed(() => execution.value?.vehicle?.id || vehicleId.value || null);
const nodeQuery = ref('');
const nodeOptions = computed(() => {
  const nodes = mapStore.currentGraph?.nodes || [];
  const preferred = nodes.filter(node =>
    node.type === 'StandbyPoint' || node.type === 'ChargePoint' || node.type === 'JobPoint' || node.type === 'PullOverPoint'
  );
  const pool = preferred.length > 0 ? preferred : nodes;
  const keyword = nodeQuery.value.trim().toLowerCase();
  if (!keyword) return pool;
  return pool.filter(node =>
    node.id.toLowerCase().includes(keyword) || (node.name || '').toLowerCase().includes(keyword)
  );
});

function instanceStatusLabel(status?: string) {
  const key = (status || '').toLowerCase();
  if (key === 'running' || key === 'executing') return '运行中';
  return orderStatusLabel(status);
}

function defaultNode() {
  return mapStore.currentGraph?.nodes[0]?.id || '';
}

function addBlock(actionType: string, nodeId?: string) {
  const brick = palette.find(item => item.actionType === actionType) || palette[0];
  const block: TianfengBlock = {
    id: `B-${Date.now().toString(36)}-${blocks.value.length}`,
    actionType: brick.actionType,
    name: brick.name,
    category: brick.category,
    nodeId: nodeId || selectedBlock.value?.nodeId || defaultNode(),
    dwellSec: brick.dwellSec
  };
  blocks.value.push(block);
  selectedBlockId.value = block.id;
}

function onSelectNode(id: string | null) {
  if (!id) return;
  if (selectedBlock.value) {
    selectedBlock.value.nodeId = id;
    return;
  }
  addBlock('MOVE_TO_POINT', id);
}

function toneOf(actionType: string) {
  return palette.find(item => item.actionType === actionType)?.tone || '#0284c7';
}

function stepState(index: number) {
  if (!execution.value || execution.value.orderId !== traceId.value) return 'idle';
  if (index < execution.value.currentStepIndex) return 'done';
  if (index === execution.value.currentStepIndex) return 'run';
  return 'wait';
}

async function refreshLive() {
  try {
    const res = await fmsApi.listLiveWorkflows();
    liveItems.value = res.items || [];
  } catch {
    liveItems.value = [];
  }
  if (!traceId.value) return;
  try {
    execution.value = await fmsApi.getWorkflowExecution(traceId.value);
  } catch {
    execution.value = null;
  }
}

async function runChain() {
  if (!checkPermission('Fms.Workflow.Run', '在仿真中运行天风任务')) return;
  if (blocks.value.length === 0) {
    window.$message?.warning('先在地图上点站点，或从左侧加上一块动作');
    return;
  }
  running.value = true;
  try {
    const res = await fmsApi.runWorkflow({
      taskSerial: `TF-${Date.now().toString(36).toUpperCase()}`,
      vehicleId: vehicleId.value || undefined,
      priority: 80,
      steps: blocks.value.map(block => ({
        id: block.id,
        category: block.category,
        name: block.name,
        actionType: block.actionType,
        nodeId: block.nodeId,
        dwellSec: block.dwellSec,
        status: 'ready',
        desc: `${block.name} @ ${block.nodeId}`
      }))
    });
    if (!res.success || !res.orderId) {
      window.$message?.error(res.error || '下发失败');
      return;
    }
    traceId.value = res.orderId;
    router.replace({ path: '/fms/simulation', query: { trace: res.orderId } });
    window.$message?.success(`已交给仿真引擎 ${res.orderSerial}`);
    await refreshLive();
  } finally {
    running.value = false;
  }
}

async function saveScene() {
  if (!checkPermission('Fms.Simulation.Control', '保存回放')) return;
  const res = await fmsApi.captureReplay(traceId.value || undefined, 45);
  if (!res.success || !res.id) {
    window.$message?.error('没有可保存的画面');
    return;
  }
  router.push({ path: '/fms/replay', query: { id: res.id } });
}

async function follow(instanceId: string) {
  traceId.value = instanceId;
  router.replace({ path: '/fms/simulation', query: { trace: instanceId } });
  await refreshLive();
}

async function setScale(scale: number) {
  if (!checkPermission('Fms.Simulation.Control', '调整仿真倍速')) return;
  await simStore.setTimeScale(scale);
}

async function toggleClock() {
  if (!checkPermission('Fms.Simulation.Control', '控制仿真时钟')) return;
  if (simStore.isPaused) await simStore.resumeSimulation();
  else await simStore.pauseSimulation();
}

onMounted(async () => {
  await simStore.init();
  if (!mapStore.currentGraph) await mapStore.loadCurrentMap();
  if (!vehicleId.value && simStore.vehicles[0]) vehicleId.value = simStore.vehicles[0].id;
  const trace = typeof route.query.trace === 'string' ? route.query.trace : '';
  if (trace) traceId.value = trace;
  await refreshLive();
  poller = window.setInterval(refreshLive, 1000);
});

watch(
  () => route.query.trace,
  value => {
    traceId.value = typeof value === 'string' ? value : '';
    refreshLive();
  }
);

onUnmounted(() => {
  if (poller) window.clearInterval(poller);
});
</script>

<template>
  <div :class="stageClass" class="h-[calc(100vh-108px)]">
    <MapCanvas
      class="absolute inset-0"
      :graph="mapStore.currentGraph"
      :vehicles="simStore.vehicles"
      :orders="simStore.liveOrders"
      :mutex-zones="simStore.mutexZones"
      :point-cloud="mapStore.pointCloud"
      :raster="mapStore.raster"
      :selected-vehicle-id="traceVehicle"
      :selected-node-id="traceTarget"
      :layer-visibility="mapStore.layerVisibility"
      :hud-inset="{ left: 304, right: 324 }"
      @select-node="onSelectNode"
    />

    <aside class="absolute bottom-4 left-4 top-4 z-10 flex w-[280px] flex-col gap-3">
      <section :class="panelClass" class="p-3">
        <div class="text-[10px] tracking-[0.18em] text-sky-600 dark:text-cyan-300">场景仿真</div>
        <div class="mt-1 text-sm font-semibold">仿真里的天风任务</div>
        <div class="mt-2 truncate text-[11px] text-slate-500 dark:text-slate-400">
          {{ mapStore.currentGraph?.name || '未载入地图' }}
        </div>
        <div class="mt-3 flex flex-wrap gap-1.5">
          <button class="rounded-lg bg-sky-600 px-2.5 py-1 text-[11px] font-semibold text-white" @click="toggleClock">
            {{ simStore.isPaused ? '继续' : '暂停' }}
          </button>
          <button v-for="scale in [1, 2, 5]" :key="scale" class="rounded-lg border px-2 py-1 text-[11px]" :class="simStore.timeScale === scale ? 'border-sky-600 bg-sky-600 text-white' : 'border-slate-200 text-slate-600 dark:border-white/10 dark:text-slate-300'" @click="setScale(scale)">
            {{ scale }}x
          </button>
          <RouterLink to="/fms/workflow" class="rounded-lg border border-slate-200 px-2 py-1 text-[11px] text-sky-700 dark:border-white/10 dark:text-cyan-200">完整编排</RouterLink>
          <RouterLink to="/fms/replay" class="rounded-lg border border-slate-200 px-2 py-1 text-[11px] text-sky-700 dark:border-white/10 dark:text-cyan-200">监控回放</RouterLink>
        </div>
      </section>

      <section :class="panelClass" class="min-h-0 flex-1 overflow-auto p-3">
        <div class="mb-2 text-[11px] text-slate-400">动作块 · 点地图站点即可填入选中块</div>
        <div class="grid grid-cols-2 gap-1.5">
          <button
            v-for="brick in palette"
            :key="brick.actionType"
            class="rounded-xl px-2 py-2 text-left text-[11px] font-semibold text-white"
            :style="{ background: brick.tone }"
            @click="addBlock(brick.actionType)"
          >
            {{ brick.name }}
          </button>
        </div>
        <label class="mt-3 block text-[11px] text-slate-400">
          指定车辆
          <select v-model="vehicleId" class="mt-1 w-full rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-xs text-slate-800 dark:border-white/10 dark:bg-black/30 dark:text-slate-100">
            <option value="">自动分配</option>
            <option v-for="vehicle in simStore.vehicles" :key="vehicle.id" :value="vehicle.id">
              {{ vehicle.name || vehicle.id }} · {{ vehicleStatusLabel(vehicle.status) }}
            </option>
          </select>
        </label>
        <div class="mt-3 space-y-2">
          <input
            v-model="nodeQuery"
            class="w-full rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-[11px] text-slate-700 dark:border-white/10 dark:bg-black/30 dark:text-slate-100"
            placeholder="搜索待命点 / 充电点 / 作业点"
          />
          <div v-for="(block, index) in blocks" :key="block.id" class="cursor-pointer" @click="selectedBlockId = block.id">
            <div v-if="index > 0" class="mx-auto h-3 w-px bg-slate-200 dark:bg-white/20" />
            <div class="rounded-xl border px-2.5 py-2" :class="selectedBlockId === block.id ? 'border-sky-500' : 'border-slate-200 dark:border-white/10'" :style="{ borderLeft: `4px solid ${toneOf(block.actionType)}` }">
              <div class="flex items-center justify-between text-[11px]">
                <span class="font-semibold">{{ index + 1 }}. {{ block.name }}</span>
                <span class="font-mono text-[10px]" :class="stepState(index) === 'run' ? 'text-cyan-300' : stepState(index) === 'done' ? 'text-emerald-300' : 'text-slate-500'">
                  {{ stepState(index) === 'run' ? '执行' : stepState(index) === 'done' ? '完成' : '待发' }}
                </span>
              </div>
              <select v-model="block.nodeId" class="mt-1 w-full bg-transparent text-[11px] text-sky-700 dark:text-cyan-100" @click.stop>
                <option v-for="node in nodeOptions" :key="node.id" :value="node.id" class="text-slate-900">{{ node.name || node.id }}</option>
              </select>
            </div>
          </div>
          <div v-if="blocks.length === 0" class="rounded-xl border border-dashed border-slate-300 px-3 py-6 text-center text-[11px] text-slate-500 dark:border-white/15">
            在地图上点一个站点，会生成第一块「导航」
          </div>
        </div>
        <button class="mt-3 w-full rounded-xl bg-amber-400 py-2 text-xs font-bold text-slate-950 disabled:opacity-50" :disabled="running" @click="runChain">
          {{ running ? '下发中' : '在仿真中运行' }}
        </button>
        <button class="mt-2 w-full rounded-xl border border-slate-200 py-2 text-[11px] text-slate-700 dark:border-white/15 dark:text-slate-200" @click="saveScene">保存当前画面</button>
      </section>
    </aside>

    <aside class="absolute bottom-4 right-4 top-4 z-10 flex w-[300px] flex-col gap-3">
      <section :class="panelClass" class="p-3">
        <div class="flex items-center justify-between text-[11px] text-slate-400">
          <span>运行实例</span>
          <RouterLink to="/logs/runtime" class="text-cyan-300">运行日志</RouterLink>
        </div>
        <button
          v-for="item in liveItems"
          :key="item.instanceId"
          class="mt-2 w-full rounded-xl border px-2.5 py-2 text-left"
          :class="traceId === item.instanceId ? 'border-sky-500 bg-sky-500/10' : 'border-slate-200 dark:border-white/10'"
          @click="follow(item.instanceId)"
        >
          <div class="flex justify-between text-[11px]">
            <span class="truncate font-semibold">{{ item.name }}</span>
            <span class="font-mono text-cyan-200">{{ instanceStatusLabel(item.status) }}</span>
          </div>
          <div class="mt-1 truncate text-[10px] text-slate-400">{{ item.currentStep || '—' }} · {{ item.targetNodeId || '无站点' }} · {{ item.vehicleId || '待分配' }}</div>
        </button>
        <div v-if="liveItems.length === 0" class="py-4 text-center text-[11px] text-slate-500">还没有在跑的任务</div>
      </section>
      <section :class="panelClass" class="min-h-0 flex-1 overflow-auto p-3 font-mono text-[11px] leading-5 text-slate-600 dark:text-slate-300">
        <div class="mb-2 text-slate-500">实例日志</div>
        <div v-for="(line, index) in execution?.logs || []" :key="index">{{ line }}</div>
        <div v-if="!execution?.logs?.length" class="text-slate-600">下发后，工步推进会写到这里，并进入日志中心的运行日志。</div>
      </section>
    </aside>
  </div>
</template>
