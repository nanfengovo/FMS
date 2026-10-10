<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useSimulationStore } from '@/store/modules/simulation';
import { useMapStore } from '@/store/modules/map';
import { fmsApi } from '@/service/api/fms';
import { actorLabel, auditActionLabel, formatTimestamp, humanizeStatusText } from '@/utils/fms-display';
import { signalRService } from '@/service/socket/signalr';

const simStore = useSimulationStore();
const mapStore = useMapStore();
const recentLogs = ref<Fms.AuditLogEntry[]>([]);

const engineOnline = computed(() => signalRService.isConnected.value);
const heartbeatOk = computed(() => signalRService.isConnected.value && !simStore.isPaused);
const routingLive = computed(() => signalRService.isConnected.value && !simStore.isPaused);

const alarmCount = computed(() => {
  const stopped = simStore.vehicles.filter(vehicle => {
    const status = vehicle.status.toLowerCase();
    return status === 'estop' || status === 'error';
  }).length;
  return stopped + simStore.activeConflicts.length;
});

const zonesCount = computed(() => {
  return mapStore.currentGraph?.mutexZones?.length || simStore.mutexZones?.length || 0;
});

const occupiedZonesCount = computed(() => {
  const zones = mapStore.currentGraph?.mutexZones || simStore.mutexZones || [];
  return zones.filter(z => z.currentOccupantVehicleIds && z.currentOccupantVehicleIds.length > 0).length;
});

function formatTime(iso: string) {
  return formatTimestamp(iso);
}

async function loadDashboard() {
  await simStore.init();
  if (!mapStore.currentGraph) {
    await mapStore.loadCurrentMap();
  }
  try {
    recentLogs.value = (await fmsApi.getAuditLogs(6)) || [];
  } catch {
    recentLogs.value = [];
  }
}

onMounted(() => {
  loadDashboard();
});
</script>

<template>
  <div class="mx-auto w-full max-w-[1440px] space-y-4 px-5 py-4 pb-8">
    <div class="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
      <div>
        <div class="flex items-center gap-2">
          <h1 class="text-lg font-semibold tracking-tight text-slate-900 dark:text-slate-50">态势大盘</h1>
          <span
            class="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium"
            :class="engineOnline
              ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
              : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'"
          >
            <span class="h-1.5 w-1.5 rounded-full" :class="engineOnline ? 'bg-emerald-500' : 'bg-slate-400'" />
            {{ engineOnline ? '引擎在线' : '引擎离线' }}
          </span>
        </div>
        <p class="mt-1 text-sm text-slate-500 dark:text-slate-400">车队、任务、互斥区和最近一次审计。</p>
      </div>
      <div class="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:flex">
        <RouterLink
          v-for="link in [
            { to: '/fms/screen', icon: 'i-carbon-dashboard', label: '运行大屏' },
            { to: '/fms/twin', icon: 'i-carbon-cube', label: '数字孪生', primary: true },
            { to: '/fms/tasks', icon: 'i-carbon-task', label: '任务中心' },
            { to: '/map/model', icon: 'i-carbon-map', label: '地图建模' },
            { to: '/fleet/traffic', icon: 'i-carbon-network-4', label: '交通避让' }
          ]"
          :key="link.to"
          :to="link.to"
          class="inline-flex h-9 items-center justify-center gap-1.5 rounded-lg px-3 text-sm font-medium transition"
          :class="link.primary
            ? 'bg-sky-600 text-white hover:bg-sky-500'
            : 'border border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-slate-800'"
        >
          <div :class="link.icon" class="text-base" />
          {{ link.label }}
        </RouterLink>
      </div>
    </div>

    <!-- 4 Core KPI Metric Cards -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <!-- Card 1: 在线车队 -->
      <div class="group relative overflow-hidden rounded-2xl border border-slate-200/90 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-5 shadow-sm dark:shadow-xl transition-all hover:border-sky-500/40">
        <div class="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-sky-500 to-cyan-400" />
        <div class="flex items-center justify-between mb-3">
          <span class="text-12px font-bold text-slate-500 dark:text-slate-400">在线车队</span>
          <div class="flex h-8 w-8 items-center justify-center rounded-xl bg-sky-500/10 dark:bg-sky-500/20 text-sky-600 dark:text-sky-400 border border-sky-500/20">
            <div class="i-carbon-delivery-truck text-base" />
          </div>
        </div>
        <div class="flex items-baseline gap-2 mb-3">
          <span class="font-mono text-32px font-extrabold text-sky-600 dark:text-sky-400 tracking-tight">{{ simStore.vehicles.length }}</span>
          <span class="text-11px text-slate-500 dark:text-slate-400 font-medium font-mono">台就绪</span>
        </div>
        <div class="flex items-center justify-between text-11px font-mono text-slate-500 dark:text-slate-400 border-t border-slate-100 dark:border-slate-800/80 pt-2.5">
          <span>运行中: <strong class="text-emerald-600 dark:text-emerald-400">{{ simStore.fleetStats.running }}</strong></span>
          <span>待命中: <strong class="text-slate-700 dark:text-slate-300">{{ simStore.fleetStats.idle }}</strong></span>
          <span>充/故障: <strong :class="simStore.fleetStats.error > 0 ? 'text-red-500 dark:text-red-400 font-bold' : 'text-slate-400'">{{ simStore.fleetStats.charging }}/{{ simStore.fleetStats.error }}</strong></span>
        </div>
      </div>

      <!-- Card 2: 任务吞吐 -->
      <div class="group relative overflow-hidden rounded-2xl border border-slate-200/90 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-5 shadow-sm dark:shadow-xl transition-all hover:border-amber-500/40">
        <div class="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-amber-500 to-orange-400" />
        <div class="flex items-center justify-between mb-3">
          <span class="text-12px font-bold text-slate-500 dark:text-slate-400">活跃任务</span>
          <div class="flex h-8 w-8 items-center justify-center rounded-xl bg-amber-500/10 dark:bg-amber-500/20 text-amber-600 dark:text-amber-400 border border-amber-500/20">
            <div class="i-carbon-task text-base" />
          </div>
        </div>
        <div class="flex items-baseline gap-2 mb-3">
          <span class="font-mono text-32px font-extrabold text-amber-600 dark:text-amber-400 tracking-tight">{{ simStore.liveOrders.length }}</span>
          <span class="text-11px text-slate-500 dark:text-slate-400 font-medium font-mono">件派发中</span>
        </div>
        <div class="flex items-center justify-between text-11px font-mono text-slate-500 dark:text-slate-400 border-t border-slate-100 dark:border-slate-800/80 pt-2.5">
          <span>调度心跳 <strong :class="heartbeatOk ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'">{{ heartbeatOk ? '正常' : (simStore.isPaused ? '已暂停' : '断开') }}</strong></span>
          <span>动态改道 <strong :class="routingLive ? 'text-slate-700 dark:text-slate-300' : 'text-slate-400'">{{ routingLive ? '运行中' : '未运行' }}</strong></span>
        </div>
      </div>

      <!-- Card 3: 互斥锁区 -->
      <div class="group relative overflow-hidden rounded-2xl border border-slate-200/90 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-5 shadow-sm dark:shadow-xl transition-all hover:border-purple-500/40">
        <div class="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-purple-500 to-indigo-400" />
        <div class="flex items-center justify-between mb-3">
          <span class="text-12px font-bold text-slate-500 dark:text-slate-400">互斥区</span>
          <div class="flex h-8 w-8 items-center justify-center rounded-xl bg-purple-500/10 dark:bg-purple-500/20 text-purple-600 dark:text-purple-400 border border-purple-500/20">
            <div class="i-carbon-road text-base" />
          </div>
        </div>
        <div class="flex items-baseline gap-2 mb-3">
          <span class="font-mono text-32px font-extrabold text-purple-600 dark:text-purple-400 tracking-tight">{{ zonesCount }}</span>
          <span class="text-11px text-slate-500 dark:text-slate-400 font-medium font-mono">个</span>
        </div>
        <div class="flex items-center justify-between text-11px font-mono text-slate-500 dark:text-slate-400 border-t border-slate-100 dark:border-slate-800/80 pt-2.5">
          <span>当前占用: <strong :class="occupiedZonesCount > 0 ? 'text-amber-500 dark:text-amber-400 font-bold' : 'text-slate-500 dark:text-slate-400'">{{ occupiedZonesCount }}</strong></span>
          <span>占用检测 <strong class="text-emerald-600 dark:text-emerald-400">已开</strong></span>
        </div>
      </div>

      <!-- Card 4: 安全联锁 -->
      <div class="group relative overflow-hidden rounded-2xl border border-slate-200/90 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-5 shadow-sm dark:shadow-xl transition-all" :class="alarmCount > 0 ? 'hover:border-red-500/40' : 'hover:border-emerald-500/40'">
        <div class="absolute top-0 left-0 right-0 h-1" :class="alarmCount > 0 ? 'bg-gradient-to-r from-red-500 to-rose-400' : 'bg-gradient-to-r from-emerald-500 to-teal-400'" />
        <div class="flex items-center justify-between mb-3">
          <span class="text-12px font-bold text-slate-500 dark:text-slate-400">安全联锁</span>
          <div class="flex h-8 w-8 items-center justify-center rounded-xl" :class="alarmCount > 0 ? 'bg-red-500/10 dark:bg-red-500/20 text-red-600 dark:text-red-400 border border-red-500/20' : 'bg-emerald-500/10 dark:bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20'">
            <div :class="alarmCount > 0 ? 'i-carbon-locked' : 'i-carbon-unlocked'" class="text-base" />
          </div>
        </div>
        <div class="flex items-baseline gap-2 mb-3">
          <span class="font-mono text-32px font-extrabold tracking-tight" :class="alarmCount > 0 ? 'text-red-600 dark:text-red-400' : 'text-emerald-600 dark:text-emerald-400'">
            {{ alarmCount }}
          </span>
          <span class="text-11px font-medium font-mono" :class="alarmCount > 0 ? 'text-red-600 dark:text-red-400' : 'text-emerald-600 dark:text-emerald-400'">
            {{ alarmCount > 0 ? '项告警' : '回路正常' }}
          </span>
        </div>
        <div class="flex items-center justify-between text-11px font-mono text-slate-500 dark:text-slate-400 border-t border-slate-100 dark:border-slate-800/80 pt-2.5">
          <span>急停/故障: <strong class="text-slate-800 dark:text-slate-200">{{ alarmCount - simStore.activeConflicts.length }}</strong></span>
          <span>冲突预警: <strong :class="simStore.activeConflicts.length > 0 ? 'text-red-500 dark:text-red-400 font-bold' : 'text-slate-400'">{{ simStore.activeConflicts.length }}</strong></span>
        </div>
      </div>
    </div>

    <!-- Center Section: Real-time Topology & Status Grid -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-5">
      <!-- Left 2 cols: System Topology Overview & Navigation -->
      <div class="lg:col-span-2 rounded-2xl border border-slate-200/90 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-6 shadow-sm dark:shadow-xl space-y-5">
        <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800/80 pb-4">
          <div class="flex items-center gap-2.5">
            <div class="i-carbon-network-3 text-xl text-sky-600 dark:text-sky-400" />
            <h2 class="text-sm font-semibold text-slate-900 dark:text-slate-100">路网概况</h2>
          </div>
          <RouterLink to="/fms/twin" class="text-12px font-medium text-sky-600 dark:text-sky-400 hover:underline flex items-center gap-1 font-mono">
            <span>打开孪生</span>
            <div class="i-carbon-arrow-right text-xs" />
          </RouterLink>
        </div>

        <div class="grid grid-cols-3 gap-4 font-mono">
          <div class="rounded-xl bg-slate-50 dark:bg-black/30 border border-slate-200 dark:border-slate-800 p-4 transition-all hover:border-sky-500/40 shadow-sm">
            <div class="text-slate-500 dark:text-slate-400 text-11px mb-1.5 flex items-center gap-1.5">
              <span class="h-2 w-2 rounded-full bg-sky-500" />
              <span>全局拓扑节点</span>
            </div>
            <div class="text-24px font-extrabold text-slate-900 dark:text-white">
              {{ mapStore.currentGraph?.nodes?.length || 0 }}
              <span class="text-12px font-normal text-slate-400">个</span>
            </div>
          </div>
          <div class="rounded-xl bg-slate-50 dark:bg-black/30 border border-slate-200 dark:border-slate-800 p-4 transition-all hover:border-emerald-500/40 shadow-sm">
            <div class="text-slate-500 dark:text-slate-400 text-11px mb-1.5 flex items-center gap-1.5">
              <span class="h-2 w-2 rounded-full bg-emerald-500" />
              <span>车道</span>
            </div>
            <div class="text-24px font-extrabold text-emerald-600 dark:text-emerald-400">
              {{ mapStore.currentGraph?.paths?.length || 0 }}
              <span class="text-12px font-normal text-slate-400">条</span>
            </div>
          </div>
          <div class="rounded-xl bg-slate-50 dark:bg-black/30 border border-slate-200 dark:border-slate-800 p-4 transition-all hover:border-cyan-500/40 shadow-sm">
            <div class="text-slate-500 dark:text-slate-400 text-11px mb-1.5 flex items-center gap-1.5">
              <span class="h-2 w-2 rounded-full bg-cyan-500" />
              <span>激光点云</span>
            </div>
            <div class="text-24px font-extrabold text-cyan-600 dark:text-cyan-400">
              {{ mapStore.stats.pointCloud.toLocaleString() }}
              <span class="text-12px font-normal text-slate-400">点</span>
            </div>
          </div>
        </div>

        <!-- Quick Subsystem Entry Grid -->
        <div class="pt-2">
          <div class="mb-3 text-xs font-medium text-slate-400">常用入口</div>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-3">
            <RouterLink
              to="/map/zones"
              class="group rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-black/20 p-3.5 hover:border-purple-500/40 hover:shadow-sm transition-all block active:scale-[0.98]"
            >
              <div class="i-carbon-road text-22px text-purple-600 dark:text-purple-400 mb-1.5 group-hover:scale-110 transition-transform" />
              <div class="text-13px font-bold text-slate-900 dark:text-white">库区与互斥</div>
              <div class="text-11px text-slate-500 dark:text-slate-400">查看占用和释放</div>
            </RouterLink>
            <RouterLink
              to="/fleet/traffic"
              class="group rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-black/20 p-3.5 hover:border-sky-500/40 hover:shadow-sm transition-all block active:scale-[0.98]"
            >
              <div class="i-carbon-settings-adjust text-22px text-sky-600 dark:text-sky-400 mb-1.5 group-hover:scale-110 transition-transform" />
              <div class="text-13px font-bold text-slate-900 dark:text-white">交通避让</div>
              <div class="text-11px text-slate-500 dark:text-slate-400">速度、间距和超时</div>
            </RouterLink>
            <RouterLink
              to="/system/rcs"
              class="group rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-black/20 p-3.5 hover:border-emerald-500/40 hover:shadow-sm transition-all block active:scale-[0.98]"
            >
              <div class="i-carbon-network-4 text-22px text-emerald-600 dark:text-emerald-400 mb-1.5 group-hover:scale-110 transition-transform" />
              <div class="text-13px font-bold text-slate-900 dark:text-white">接口集成</div>
              <div class="text-11px text-slate-500 dark:text-slate-400">任务类型和回调</div>
            </RouterLink>
            <RouterLink
              to="/system/audit"
              class="group rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-black/20 p-3.5 hover:border-amber-500/40 hover:shadow-sm transition-all block active:scale-[0.98]"
            >
              <div class="i-carbon-document text-22px text-amber-600 dark:text-amber-400 mb-1.5 group-hover:scale-110 transition-transform" />
              <div class="text-13px font-bold text-slate-900 dark:text-white">安全审计</div>
              <div class="text-11px text-slate-500 dark:text-slate-400">登录和操作记录</div>
            </RouterLink>
          </div>
        </div>
      </div>

      <!-- Right 1 col: Recent Audit Events Feed -->
      <div class="rounded-2xl border border-slate-200/80 dark:border-slate-800/80 bg-white dark:bg-[#11151f] p-6 shadow-sm dark:shadow-xl flex flex-col">
        <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800/80 pb-4 mb-4">
          <div class="flex items-center gap-2.5">
            <div class="i-carbon-notebook text-xl text-amber-500" />
            <h2 class="text-sm font-semibold text-slate-900 dark:text-slate-100">最近审计</h2>
          </div>
          <RouterLink to="/system/audit" class="text-12px font-medium text-amber-600 dark:text-amber-400 hover:underline flex items-center gap-1">
            <span>全部日志</span>
            <div class="i-carbon-arrow-right text-xs" />
          </RouterLink>
        </div>

        <div class="flex-1 space-y-3 overflow-y-auto max-h-[340px] pr-1">
          <div
            v-if="recentLogs.length === 0"
            class="flex h-40 items-center justify-center rounded-xl border border-dashed border-slate-200 dark:border-slate-800 text-12px text-slate-400"
          >
            暂无审计事件
          </div>
          <div
            v-for="log in recentLogs"
            :key="log.id"
            class="rounded-xl border border-slate-100 dark:border-slate-800/80 bg-slate-50/70 dark:bg-black/30 p-3 font-mono text-11px transition-all hover:border-slate-300 dark:hover:border-slate-700"
          >
            <div class="flex items-center justify-between mb-1.5">
              <span
                class="text-10px font-bold uppercase rounded-md px-2 py-0.5"
                :class="log.success ? 'bg-blue-500/10 text-blue-600 dark:bg-blue-500/20 dark:text-blue-400' : 'bg-red-500/10 text-red-600 dark:bg-red-500/20 dark:text-red-400'"
              >
                {{ auditActionLabel(log.actionType) }}
              </span>
              <span class="text-10px text-slate-400">{{ formatTime(log.timestamp) }}</span>
            </div>
            <div class="text-slate-700 dark:text-slate-300 text-12px line-clamp-2 leading-relaxed">
              {{ humanizeStatusText(log.details) }}
            </div>
            <div class="text-10px text-slate-400 dark:text-slate-500 mt-1 flex items-center gap-1">
              <span class="i-carbon-user text-xs" />
              <span>{{ actorLabel(log.operator) }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
