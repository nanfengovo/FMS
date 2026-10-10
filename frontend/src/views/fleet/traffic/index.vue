<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { fmsApi } from '@/service/api/fms';

const saving = ref(false);
const statusMsg = ref('');
const isSuccess = ref(true);

// Traffic Configuration
const trafficForm = ref<Fms.TrafficConfig>({
  maxSpeedLimit: 1.8,
  defaultTurningRadius: 1.0,
  deadlockTimeoutSec: 20,
  collisionSafetyRadiusMeters: 0.8,
  highDensityThreshold: 4,
  priorityWeights: {}
});

// Advanced Rules
const emptyYieldLoaded = ref(true);
const avoidanceStrategy = ref<'spur' | 'yield' | 'reroute'>('spur');
const corridorSafetyBuffer = ref(0.6);

// Industry Profiles
interface ProfilePreset {
  id: string;
  name: string;
  desc: string;
  icon: string;
  maxSpeed: number;
  turningRadius: number;
  deadlockTimeout: number;
  safetyRadius: number;
  density: number;
}

const presets: ProfilePreset[] = [
  {
    id: 'standard',
    name: '标准综合工况',
    desc: '适用于常规仓储配送，速度与安全均衡，兼顾能耗与节拍',
    icon: 'i-carbon-meter-alt',
    maxSpeed: 1.8,
    turningRadius: 1.0,
    deadlockTimeout: 20,
    safetyRadius: 0.8,
    density: 4
  },
  {
    id: 'high_speed',
    name: '极速穿梭工况',
    desc: '全封闭无人自动化库区，最高限速放开，追求每小时最高搬运吞吐量',
    icon: 'i-carbon-rocket',
    maxSpeed: 2.8,
    turningRadius: 1.2,
    deadlockTimeout: 10,
    safetyRadius: 0.6,
    density: 6
  },
  {
    id: 'safety_first',
    name: '人机混行防御',
    desc: '车间通道有人工作业，扩大碰撞包络与盲区刹停阈值，低速谨慎行驶',
    icon: 'i-carbon-security',
    maxSpeed: 0.9,
    turningRadius: 0.8,
    deadlockTimeout: 35,
    safetyRadius: 1.4,
    density: 3
  },
  {
    id: 'narrow_aisle',
    name: '密集窄道工况',
    desc: '货架间距狭窄且多交叉路口，缩短死锁仲裁窗口，强化支线提前避让',
    icon: 'i-carbon-roadmap',
    maxSpeed: 1.2,
    turningRadius: 0.6,
    deadlockTimeout: 12,
    safetyRadius: 0.45,
    density: 2
  }
];

const activePresetId = ref('standard');

function applyPreset(p: ProfilePreset) {
  activePresetId.value = p.id;
  trafficForm.value.maxSpeedLimit = p.maxSpeed;
  trafficForm.value.defaultTurningRadius = p.turningRadius;
  trafficForm.value.deadlockTimeoutSec = p.deadlockTimeout;
  trafficForm.value.collisionSafetyRadiusMeters = p.safetyRadius;
  trafficForm.value.highDensityThreshold = p.density;
}

// SVG Dynamic Visualizer calculations
const svgScale = 60; // 60px = 1m
const amrLengthPx = 1.2 * svgScale; // 72px
const amrWidthPx = 0.8 * svgScale;  // 48px

const stopZoneRadiusPx = computed(() => {
  return (trafficForm.value.collisionSafetyRadiusMeters * 0.7 + 0.5) * svgScale;
});

const warningZoneRadiusPx = computed(() => {
  return (trafficForm.value.collisionSafetyRadiusMeters + 0.6) * svgScale;
});

const speedVectorLengthPx = computed(() => {
  return 20 + trafficForm.value.maxSpeedLimit * 25;
});

function toUiAvoidance(value: number | string | undefined): 'spur' | 'yield' | 'reroute' {
  if (value === 1 || value === 'WaitInPlace' || value === 'yield') return 'yield';
  if (value === 3 || value === 'DynamicReroute' || value === 'reroute') return 'reroute';
  return 'spur';
}

function toApiAvoidance(value: 'spur' | 'yield' | 'reroute') {
  if (value === 'yield') return 1;
  if (value === 'reroute') return 3;
  return 2;
}

async function loadConfig() {
  try {
    const bundle = await fmsApi.getConfigBundle();
    if (bundle.trafficConfig) {
      trafficForm.value = { ...bundle.trafficConfig };
      emptyYieldLoaded.value = bundle.trafficConfig.emptyYieldLoaded ?? true;
      avoidanceStrategy.value = toUiAvoidance(bundle.trafficConfig.defaultAvoidanceStrategy);
      corridorSafetyBuffer.value = bundle.trafficConfig.corridorSafetyBuffer ?? bundle.trafficConfig.collisionSafetyRadiusMeters ?? 0.6;
      trafficForm.value.collisionSafetyRadiusMeters = corridorSafetyBuffer.value;
    }
  } catch (err: any) {
    statusMsg.value = `加载失败: ${err.message}`;
    isSuccess.value = false;
  }
}

async function saveConfig() {
  saving.value = true;
  statusMsg.value = '';
  try {
    const res = await fmsApi.updateTrafficConfig(
      {
        ...trafficForm.value,
        emptyYieldLoaded: emptyYieldLoaded.value,
        defaultAvoidanceStrategy: toApiAvoidance(avoidanceStrategy.value),
        corridorSafetyBuffer: corridorSafetyBuffer.value,
        collisionSafetyRadiusMeters: corridorSafetyBuffer.value
      },
      'Admin'
    );
    isSuccess.value = res.success;
    statusMsg.value = res.message || '交通控制策略已成功热更新至调度核心！';
    setTimeout(() => {
      statusMsg.value = '';
    }, 4000);
  } catch (err: any) {
    isSuccess.value = false;
    statusMsg.value = `保存失败: ${err.message}`;
  } finally {
    saving.value = false;
  }
}

onMounted(() => {
  loadConfig();
});
</script>

<template>
  <div class="space-y-6 p-6 pb-12 max-w-6xl mx-auto">
    <!-- Header Banner -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-4">
      <div class="flex items-center gap-4">
        <div class="flex h-12 w-12 items-center justify-center rounded-xl bg-sky-500/10 text-sky-500 border border-sky-500/20">
          <div class="i-carbon-settings-adjust text-2xl" />
        </div>
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-lg font-semibold text-slate-900 dark:text-white">交通避让</h1>
            <span class="rounded bg-sky-50 dark:bg-sky-950/60 border border-sky-200 dark:border-sky-800 px-2.5 py-0.5 text-11px font-mono text-sky-600 dark:text-sky-400">
              策略引擎已启用
            </span>
          </div>
          <p class="text-13px text-slate-500 dark:text-slate-400 mt-1">全局动态限速、多车死锁防呆预警超时、避让仲裁与物理碰撞包络标定</p>
        </div>
      </div>

      <button
        class="flex items-center gap-2 rounded-xl bg-sky-600 px-6 py-2.5 text-13px font-semibold text-white shadow-md shadow-sky-600/25 hover:bg-sky-500 active:scale-95 transition-all disabled:opacity-50"
        :disabled="saving"
        @click="saveConfig"
      >
        <div class="i-carbon-save text-base" :class="{ 'animate-spin': saving }" />
        <span>{{ saving ? '正在同步核心引擎...' : '保存策略并热加载' }}</span>
      </button>
    </div>

    <!-- Feedback Banner -->
    <div v-if="statusMsg" class="rounded-xl border p-4 text-13px font-medium flex items-center gap-3 shadow-sm transition-all" :class="isSuccess ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-800 dark:text-emerald-300' : 'border-rose-500/30 bg-rose-500/10 text-rose-800 dark:text-rose-300'">
      <div class="text-xl" :class="isSuccess ? 'i-carbon-checkmark-filled text-emerald-500' : 'i-carbon-warning-filled text-rose-500'" />
      <span>{{ statusMsg }}</span>
    </div>

    <!-- Section 1: Industry Preset Cards -->
    <div class="space-y-3">
      <div class="flex items-center justify-between">
        <h2 class="text-13px font-bold text-slate-900 dark:text-white uppercase tracking-wider font-mono">
          行业工况预设矩阵 (Industrial Presets)
        </h2>
        <span class="text-11px text-slate-400">点选一套预设，再保存热加载</span>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <div
          v-for="p in presets"
          :key="p.id"
          class="rounded-2xl border p-4 cursor-pointer transition-all flex flex-col justify-between"
          :class="activePresetId === p.id ? 'border-sky-500 bg-sky-50/50 dark:bg-sky-950/20 shadow-md ring-1 ring-sky-500/30' : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] hover:border-slate-300 dark:hover:border-slate-700'"
          @click="applyPreset(p)"
        >
          <div>
            <div class="flex items-center justify-between mb-2">
              <div class="flex h-8 w-8 items-center justify-center rounded-lg" :class="activePresetId === p.id ? 'bg-sky-500 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400'">
                <div :class="p.icon" class="text-lg" />
              </div>
              <span v-if="activePresetId === p.id" class="text-10px font-mono font-bold text-sky-600 dark:text-sky-400 bg-sky-100 dark:bg-sky-900/60 px-1.5 py-0.5 rounded">
                生效中
              </span>
            </div>
            <h3 class="text-13px font-bold text-slate-900 dark:text-white">{{ p.name }}</h3>
            <p class="text-11px text-slate-500 dark:text-slate-400 mt-1 line-clamp-2 leading-relaxed">{{ p.desc }}</p>
          </div>

          <div class="mt-4 pt-3 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between text-11px font-mono text-slate-500">
            <span>限速: {{ p.maxSpeed }}m/s</span>
            <span>包络: {{ p.safetyRadius }}m</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Section 2: Kinematic Parameters & Safety Envelope Visualizer -->
    <div class="grid grid-cols-1 lg:grid-cols-12 gap-6">
      <!-- Left 7 cols: Precision Steppers & Sliders -->
      <div class="lg:col-span-7 space-y-4">
        <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm space-y-6">
          <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800/80 pb-4">
            <div>
              <h2 class="text-14px font-bold text-slate-900 dark:text-white">运动学限值与间距调校</h2>
              <p class="text-11px text-slate-500">支持数值微调步进输入与平滑滑动条双向联动</p>
            </div>
            <span class="text-11px font-mono text-slate-400">Unit: Metric</span>
          </div>

          <!-- Parameter 1: Max Speed -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-13px font-medium text-slate-800 dark:text-slate-200">最高速度</span>
                <p class="text-11px text-slate-400">主干道直线最高运行速度限制，转弯与避障时按曲率自动削减</p>
              </div>
              <div class="flex items-center gap-1.5">
                <input
                  v-model.number="trafficForm.maxSpeedLimit"
                  type="number"
                  step="0.1"
                  min="0.1"
                  max="5.0"
                  class="w-20 text-center rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900 py-1 text-13px font-mono font-bold text-sky-600 dark:text-sky-400 focus:outline-none focus:border-sky-500"
                />
                <span class="text-12px font-mono text-slate-400">m/s</span>
              </div>
            </div>
            <input
              v-model.number="trafficForm.maxSpeedLimit"
              type="range"
              min="0.1"
              max="5.0"
              step="0.05"
              class="w-full accent-sky-500 cursor-pointer"
            />
            <div class="flex justify-between text-10px font-mono text-slate-400">
              <span>0.1 m/s (低速蠕动/挂接)</span>
              <span>2.5 m/s (标准工况)</span>
              <span>5.0 m/s (高速专线)</span>
            </div>
          </div>

          <!-- Parameter 2: Turning Radius -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-13px font-medium text-slate-800 dark:text-slate-200">最小转弯半径</span>
                <p class="text-11px text-slate-400">贝塞尔平滑路径在弧线转弯时的离心力安全最小限制</p>
              </div>
              <div class="flex items-center gap-1.5">
                <input
                  v-model.number="trafficForm.defaultTurningRadius"
                  type="number"
                  step="0.1"
                  min="0.2"
                  max="5.0"
                  class="w-20 text-center rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900 py-1 text-13px font-mono font-bold text-sky-600 dark:text-sky-400 focus:outline-none focus:border-sky-500"
                />
                <span class="text-12px font-mono text-slate-400">m</span>
              </div>
            </div>
            <input
              v-model.number="trafficForm.defaultTurningRadius"
              type="range"
              min="0.2"
              max="5.0"
              step="0.1"
              class="w-full accent-sky-500 cursor-pointer"
            />
          </div>

          <!-- Parameter 3: Collision Safety Radius Buffer -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-13px font-medium text-slate-800 dark:text-slate-200">安全间距</span>
                <p class="text-11px text-slate-400">会车及跟车时动态包络膨胀裕量，侵入此区域即触发减速保护</p>
              </div>
              <div class="flex items-center gap-1.5">
                <input
                  v-model.number="trafficForm.collisionSafetyRadiusMeters"
                  type="number"
                  step="0.05"
                  min="0.2"
                  max="3.0"
                  class="w-20 text-center rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900 py-1 text-13px font-mono font-bold text-amber-600 dark:text-amber-400 focus:outline-none focus:border-sky-500"
                />
                <span class="text-12px font-mono text-slate-400">m</span>
              </div>
            </div>
            <input
              v-model.number="trafficForm.collisionSafetyRadiusMeters"
              type="range"
              min="0.2"
              max="3.0"
              step="0.05"
              class="w-full accent-amber-500 cursor-pointer"
            />
          </div>

          <!-- Parameter 4: Deadlock Timeout -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-13px font-medium text-slate-800 dark:text-slate-200">死锁超时</span>
                <p class="text-11px text-slate-400">多车在交叉点或窄通道等待超过此时长时触发调度仲裁</p>
              </div>
              <div class="flex items-center gap-1.5">
                <input
                  v-model.number="trafficForm.deadlockTimeoutSec"
                  type="number"
                  step="1"
                  min="1"
                  max="120"
                  class="w-20 text-center rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900 py-1 text-13px font-mono font-bold text-rose-600 dark:text-rose-400 focus:outline-none focus:border-sky-500"
                />
                <span class="text-12px font-mono text-slate-400">s</span>
              </div>
            </div>
            <input
              v-model.number="trafficForm.deadlockTimeoutSec"
              type="range"
              min="1"
              max="120"
              step="1"
              class="w-full accent-rose-500 cursor-pointer"
            />
          </div>
        </div>
      </div>

      <!-- Right 5 cols: Dynamic Safety Envelope Visualizer -->
      <div class="lg:col-span-5 space-y-4">
        <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm flex flex-col justify-between h-full">
          <div>
            <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800/80 pb-4 mb-4">
              <div>
                <h2 class="text-sm font-semibold text-slate-900 dark:text-white">安全包络</h2>
                <p class="text-11px text-slate-500">根据当前间距与速度实时推算的实车防护区</p>
              </div>
              <span class="rounded bg-slate-100 dark:bg-slate-800 px-2 py-0.5 text-10px font-mono text-slate-500">
                包络示意
              </span>
            </div>

            <!-- Interactive SVG Diagram -->
            <div class="flex justify-center items-center py-6 bg-slate-50 dark:bg-slate-900/60 rounded-xl border border-slate-100 dark:border-slate-800/80">
              <svg width="280" height="240" viewBox="0 0 280 240" class="overflow-visible">
                <!-- Center Coordinate 140, 120 -->
                <defs>
                  <pattern id="gridPattern" width="20" height="20" patternUnits="userSpaceOnUse">
                    <path d="M 20 0 L 0 0 0 20" fill="none" stroke="currentColor" class="text-slate-200 dark:text-slate-800" stroke-width="0.5" />
                  </pattern>
                </defs>
                <rect width="280" height="240" fill="url(#gridPattern)" />

                <!-- Outer Warning Zone (Amber dashed) -->
                <circle
                  cx="140"
                  cy="120"
                  :r="warningZoneRadiusPx"
                  fill="rgba(245, 158, 11, 0.08)"
                  stroke="#f59e0b"
                  stroke-width="1.5"
                  stroke-dasharray="4,4"
                  class="transition-all duration-300"
                />

                <!-- Inner E-Stop Zone (Rose dashed) -->
                <circle
                  cx="140"
                  cy="120"
                  :r="stopZoneRadiusPx"
                  fill="rgba(244, 63, 94, 0.12)"
                  stroke="#f43f5e"
                  stroke-width="1.5"
                  stroke-dasharray="3,3"
                  class="transition-all duration-300"
                />

                <!-- AGV Body Chassis -->
                <rect
                  :x="140 - amrLengthPx / 2"
                  :y="120 - amrWidthPx / 2"
                  :width="amrLengthPx"
                  :height="amrWidthPx"
                  rx="6"
                  fill="#0ea5e9"
                  stroke="#0284c7"
                  stroke-width="2"
                  class="transition-all"
                />

                <!-- Wheels -->
                <rect :x="140 - 24" :y="120 - amrWidthPx / 2 - 3" width="16" height="4" rx="1" fill="#334155" />
                <rect :x="140 - 24" :y="120 + amrWidthPx / 2 - 1" width="16" height="4" rx="1" fill="#334155" />

                <!-- Heading Arrow & Velocity Vector -->
                <line
                  x1="140"
                  y1="120"
                  :x2="140 + speedVectorLengthPx"
                  y2="120"
                  stroke="#38bdf8"
                  stroke-width="3"
                  stroke-linecap="round"
                  class="transition-all duration-300"
                />
                <polygon
                  :points="`${140 + speedVectorLengthPx + 4},120 ${140 + speedVectorLengthPx - 4},116 ${140 + speedVectorLengthPx - 4},124`"
                  fill="#38bdf8"
                />

                <!-- Label -->
                <text x="140" y="124" text-anchor="middle" font-size="10" font-family="monospace" fill="#ffffff" font-weight="bold">AGV-01</text>
              </svg>
            </div>

            <!-- Legend -->
            <div class="mt-4 space-y-2 text-11px font-mono">
              <div class="flex items-center justify-between text-rose-600 dark:text-rose-400">
                <span class="flex items-center gap-1.5">
                  <span class="h-2 w-2 rounded-full bg-rose-500" />
                  急停内停区 (E-Stop):
                </span>
                <span>{{ (trafficForm.collisionSafetyRadiusMeters * 0.7).toFixed(2) }} m (强行截断)</span>
              </div>
              <div class="flex items-center justify-between text-amber-600 dark:text-amber-400">
                <span class="flex items-center gap-1.5">
                  <span class="h-2 w-2 rounded-full bg-amber-500" />
                  减速预警区 (Decel Buffer):
                </span>
                <span>{{ trafficForm.collisionSafetyRadiusMeters.toFixed(2) }} m (梯级减速)</span>
              </div>
              <div class="flex items-center justify-between text-sky-600 dark:text-sky-400">
                <span class="flex items-center gap-1.5">
                  <span class="h-2 w-2 rounded-full bg-sky-500" />
                  速度矢量推力:
                </span>
                <span>{{ trafficForm.maxSpeedLimit }} m/s 航向</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Section 3: Deadlock Arbitration & Avoidance Rules -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
      <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800/80 pb-4 mb-5">
        <div class="flex items-center gap-3">
          <div class="flex h-9 w-9 items-center justify-center rounded-lg bg-indigo-500/10 text-indigo-500">
            <div class="i-carbon-flow-stream text-lg" />
          </div>
          <div>
            <h2 class="text-sm font-semibold text-slate-900 dark:text-white">死锁与避让</h2>
            <p class="text-11px text-slate-500">当两台及以上车辆在单轨或十字路口形成闭环死锁阻滞时的脱困决策树</p>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <!-- Strategy 1 -->
        <label
          class="rounded-xl border p-4 cursor-pointer transition-all flex flex-col justify-between"
          :class="avoidanceStrategy === 'spur' ? 'border-sky-500 bg-sky-50/40 dark:bg-sky-950/20 ring-1 ring-sky-500/30' : 'border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40'"
        >
          <div class="flex items-start justify-between">
            <div>
              <span class="text-13px font-bold text-slate-900 dark:text-white block">支线侧向避让 (Pull-over Spur)</span>
              <p class="text-11px text-slate-500 mt-1">低优先级车辆驶入就近侧向支路或泊位，主线高优先级车辆无感通行</p>
            </div>
            <input v-model="avoidanceStrategy" type="radio" value="spur" class="text-sky-600 mt-1" />
          </div>
          <span class="mt-3 inline-block rounded bg-sky-100 dark:bg-sky-900/60 px-2 py-0.5 text-10px font-mono text-sky-700 dark:text-sky-300 font-semibold self-start">
            推荐工业标配
          </span>
        </label>

        <!-- Strategy 2 -->
        <label
          class="rounded-xl border p-4 cursor-pointer transition-all flex flex-col justify-between"
          :class="avoidanceStrategy === 'reroute' ? 'border-sky-500 bg-sky-50/40 dark:bg-sky-950/20 ring-1 ring-sky-500/30' : 'border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40'"
        >
          <div class="flex items-start justify-between">
            <div>
              <span class="text-sm font-semibold text-slate-900 dark:text-white block">动态改道</span>
              <p class="text-11px text-slate-500 mt-1">调用时间窗口 A* 算法，后方阻塞车辆立即放弃当前拓扑路径绕远路通行</p>
            </div>
            <input v-model="avoidanceStrategy" type="radio" value="reroute" class="text-sky-600 mt-1" />
          </div>
          <span class="mt-3 inline-block rounded bg-slate-100 dark:bg-slate-800 px-2 py-0.5 text-10px font-mono text-slate-600 dark:text-slate-400 self-start">
            环网网状地图适用
          </span>
        </label>

        <!-- Strategy 3 -->
        <label
          class="rounded-xl border p-4 cursor-pointer transition-all flex flex-col justify-between"
          :class="avoidanceStrategy === 'yield' ? 'border-sky-500 bg-sky-50/40 dark:bg-sky-950/20 ring-1 ring-sky-500/30' : 'border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40'"
        >
          <div class="flex items-start justify-between">
            <div>
              <span class="text-sm font-semibold text-slate-900 dark:text-white block">降速等待</span>
              <p class="text-11px text-slate-500 mt-1">后行车辆在安全缓冲区外挂起，等待前方占道车辆释放闭锁资源</p>
            </div>
            <input v-model="avoidanceStrategy" type="radio" value="yield" class="text-sky-600 mt-1" />
          </div>
          <span class="mt-3 inline-block rounded bg-slate-100 dark:bg-slate-800 px-2 py-0.5 text-10px font-mono text-slate-600 dark:text-slate-400 self-start">
            单干线排队工况
          </span>
        </label>
      </div>

      <!-- Extra Safe Toggles -->
      <div class="mt-5 pt-4 border-t border-slate-100 dark:border-slate-800/80 flex flex-col md:flex-row items-center justify-between gap-4">
        <label class="flex items-center gap-3 cursor-pointer">
          <input v-model="emptyYieldLoaded" type="checkbox" class="rounded border-slate-300 text-sky-600 focus:ring-sky-500 h-4 w-4" />
          <div>
            <span class="text-13px font-medium text-slate-800 dark:text-slate-200">启用「空载车主动让行重载车」调度优先权</span>
            <p class="text-11px text-slate-400">重载载具拥有更高惯性与工位时限要求，空载就位车辆必须礼让重载车优先通过互斥区</p>
          </div>
        </label>

        <span class="text-11px font-mono text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-500/30 px-3 py-1 rounded-full whitespace-nowrap">
          Wait-For-Graph Cycle Guard Active
        </span>
      </div>
    </div>
  </div>
</template>
