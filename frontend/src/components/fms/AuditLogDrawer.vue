<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue';
import { fmsApi } from '@/service/api/fms';

const props = defineProps<{
  visible: boolean;
}>();

defineEmits<{
  (e: 'close'): void;
}>();

const logs = ref<Fms.AuditLogEntry[]>([]);
const loading = ref(false);
const selectedActionType = ref('');
const autoRefresh = ref(true);
let timer: any = null;

async function fetchLogs() {
  loading.value = true;
  try {
    const list = await fmsApi.getAuditLogs(100, selectedActionType.value || undefined);
    logs.value = list;
  } catch (e) {
    console.error('Failed to fetch audit logs:', e);
  } finally {
    loading.value = false;
  }
}

function formatTime(iso: string) {
  if (!iso) return '';
  const d = new Date(iso);
  return d.toLocaleTimeString() + '.' + String(d.getMilliseconds()).padStart(3, '0');
}

function getActionBadgeClass(type: string) {
  switch (type) {
    case 'MAP_MODIFY':
      return 'bg-blue-500/20 text-blue-400 border border-blue-500/30';
    case 'FORCE_RELEASE_MUTEX':
      return 'bg-amber-500/20 text-amber-400 border border-amber-500/30';
    case 'CONFIG_UPDATE':
      return 'bg-purple-500/20 text-purple-400 border border-purple-500/30';
    case 'VEHICLE_ESTOP':
      return 'bg-red-500/20 text-red-400 border border-red-500/30';
    case 'DISPATCH_TASK':
      return 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
    default:
      return 'bg-gray-700/40 text-gray-300';
  }
}

watch(
  () => props.visible,
  val => {
    if (val) {
      fetchLogs();
      if (autoRefresh.value) {
        timer = setInterval(fetchLogs, 3000);
      }
    } else {
      if (timer) clearInterval(timer);
    }
  }
);

watch(
  () => autoRefresh.value,
  val => {
    if (timer) clearInterval(timer);
    if (val && props.visible) {
      timer = setInterval(fetchLogs, 3000);
    }
  }
);

onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div v-if="visible" class="fixed inset-y-0 right-0 z-50 flex w-[640px] flex-col border-l border-gray-800 bg-[#12141a]/95 text-gray-200 shadow-2xl backdrop-blur-2xl transition-all">
    <!-- Header -->
    <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
      <div class="flex items-center gap-3">
        <div class="flex h-8 w-8 items-center justify-center rounded-md bg-amber-500/20 text-amber-400">
          <div class="i-carbon-notebook text-lg" />
        </div>
        <div>
          <div class="font-mono text-15px font-bold text-white">审计与调度决策日志 (Audit & Decision Logs)</div>
          <div class="text-11px text-gray-400">操作追溯、安全联锁阻断与核心调度事件</div>
        </div>
      </div>
      <button class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors" @click="$emit('close')">
        <div class="i-carbon-close text-lg" />
      </button>
    </div>

    <!-- Toolbar -->
    <div class="flex items-center justify-between border-b border-gray-800 bg-black/40 px-6 py-3">
      <div class="flex items-center gap-2">
        <span class="text-11px text-gray-400">过滤类型:</span>
        <select
          v-model="selectedActionType"
          class="rounded border border-gray-700 bg-black/60 px-2 py-1 text-12px font-mono text-gray-200 focus:border-amber-500 focus:outline-none"
          @change="fetchLogs"
        >
          <option value="">全部事件</option>
          <option value="USER_LOGIN_SUCCESS">登录成功 (USER_LOGIN_SUCCESS)</option>
          <option value="USER_LOGIN_FAILED">登录失败 (USER_LOGIN_FAILED)</option>
          <option value="ORDER_STARTED">订单启动 (ORDER_STARTED)</option>
          <option value="ORDER_COMPLETED">订单完成 (ORDER_COMPLETED)</option>
          <option value="ORDER_SUBMITTED">订单提交 (ORDER_SUBMITTED)</option>
          <option value="UPDATE_CONFIG">配置更新 (UPDATE_CONFIG)</option>
          <option value="FORCE_RELEASE_MUTEX">互斥区强制释放 (FORCE_RELEASE_MUTEX)</option>
          <option value="PUBLISH_ROAD_NETWORK">发布路网 (PUBLISH_ROAD_NETWORK)</option>
        </select>
      </div>

      <div class="flex items-center gap-3">
        <label class="flex items-center gap-1.5 text-11px text-gray-400 cursor-pointer">
          <input v-model="autoRefresh" type="checkbox" class="accent-amber-500" />
          <span>自动轮询 (3s)</span>
        </label>
        <button
          class="flex items-center gap-1 rounded bg-gray-800 px-2.5 py-1 text-11px text-gray-200 hover:bg-gray-700 transition-colors"
          :disabled="loading"
          @click="fetchLogs"
        >
          <div class="i-carbon-renew text-xs" :class="{ 'animate-spin': loading }" />
          刷新
        </button>
      </div>
    </div>

    <!-- Log List -->
    <div class="flex-1 overflow-y-auto p-4 space-y-2">
      <div v-if="loading && logs.length === 0" class="flex h-40 items-center justify-center text-gray-400">
        <div class="i-carbon-circle-dash animate-spin text-2xl mr-2" /> 正在获取审计记录...
      </div>

      <div
        v-for="log in logs"
        :key="log.id"
        class="rounded-lg border p-3 font-mono text-12px transition-all"
        :class="log.success ? 'border-gray-800 bg-black/40 hover:border-gray-700' : 'border-red-500/40 bg-red-950/20'"
      >
        <div class="flex items-center justify-between mb-1.5">
          <div class="flex items-center gap-2">
            <span
              class="rounded px-1.5 py-0.5 text-10px font-bold uppercase tracking-wider"
              :class="getActionBadgeClass(log.actionType)"
            >
              {{ log.actionType }}
            </span>
            <span class="text-11px text-gray-300 font-bold">{{ log.operator }}</span>
            <span v-if="log.entityId" class="text-10px text-gray-500">[{{ log.entityType }}: {{ log.entityId }}]</span>
          </div>
          <span class="text-10px text-gray-500">{{ formatTime(log.timestamp) }}</span>
        </div>

        <div class="text-11px text-gray-300 break-words pl-1 border-l-2" :class="log.success ? 'border-gray-700' : 'border-red-500'">
          {{ log.details }}
        </div>

        <div v-if="!log.success" class="mt-1.5 flex items-center gap-1 text-10px font-bold text-red-400">
          <div class="i-carbon-warning-alt" />
          <span>安全联锁已拦截此操作</span>
        </div>
      </div>

      <div v-if="!loading && logs.length === 0" class="py-16 text-center text-12px text-gray-500">
        暂无符合条件的审计日志记录
      </div>
    </div>
  </div>
</template>
