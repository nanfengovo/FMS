<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import MapCanvas from '@/components/fms/MapCanvas.vue';
import TaskCreateDialog from '@/components/fms/TaskCreateDialog.vue';
import { useAuth } from '@/hooks/business/auth';
import { fmsApi } from '@/service/api/fms';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';
import { nodeCaption, orderOriginLabel, orderStatusLabel, vehicleCaption, vehicleStatusLabel } from '@/utils/fms-display';

const mapStore = useMapStore();
const simStore = useSimulationStore();
const { hasAuth } = useAuth();

const now = ref(new Date());
const slots = ref<Array<{ status?: string }>>([]);
const showCreate = ref(false);
const mapRef = ref<InstanceType<typeof MapCanvas> | null>(null);
const followingId = ref<string | null>(null);
let clock: number | undefined;

const finishedKeys = ['done', 'completed', 'failed', 'cancelled', 'canceled'];
const isLive = (status?: string) => !finishedKeys.includes((status || '').toLowerCase());

const liveOrders = computed(() => simStore.activeOrders.filter(order => isLive(order.status)));
const recentOrders = computed(() => simStore.activeOrders.filter(order => !isLive(order.status)).slice(0, 5));
const orderRunning = computed(
  () => liveOrders.value.filter(order => ['running', 'executing', 'assigned'].includes((order.status || '').toLowerCase())).length
);

const carrying = computed(() => simStore.vehicles.filter(vehicle => vehicle.holdingPayload).length);
const occupiedSlots = computed(() => slots.value.filter(slot => slot.status === 'Occupied' || slot.status === 'Reserved').length);
const faultCount = computed(() => simStore.fleetStats.error + simStore.activeConflicts.length);

const selectedVehicle = computed(() => simStore.selectedVehicle);
const blockerText = computed(() => {
  const id = selectedVehicle.value?.blockedBy;
  if (!id) return '';
  return simStore.vehicles.some(vehicle => vehicle.id === id) ? `等待 ${vehicleCaption(id, simStore.vehicles)} 通过` : '等待互斥区放行';
});
const selectedOrder = computed(() =>
  selectedVehicle.value ? simStore.activeOrders.find(order => isLive(order.status) && order.assignedVehicleId === selectedVehicle.value!.id) : null
);

function orderTone(status?: string) {
  const key = (status || '').toLowerCase();
  if (key === 'failed' || key === 'fault') return 'bg-red-700';
  if (key === 'cancelled' || key === 'canceled') return 'bg-slate-300 dark:bg-slate-600';
  if (key === 'waitingaction' || key === 'paused') return 'bg-amber-600';
  if (key === 'queued' || key === 'pending') return 'bg-slate-300 dark:bg-slate-600';
  if (key === 'done' || key === 'completed') return 'bg-emerald-600';
  return 'bg-blue-600';
}

function stepLabel(name?: string) {
  return (name || '').replace(/^\d+[.、]\s*/, '');
}

function percent(order: Fms.OrderDto) {
  return Math.round((order.progress ?? 0) * 100);
}

const figures = computed(() => [
  { label: '在线', value: simStore.fleetStats.total },
  { label: '运行', value: simStore.fleetStats.running },
  { label: '待命', value: simStore.fleetStats.idle },
  { label: '充电', value: simStore.fleetStats.charging },
  { label: '载货', value: carrying.value },
  { label: '任务', value: liveOrders.value.length },
  { label: '异常', value: faultCount.value, alert: faultCount.value > 0 }
]);

function pickVehicle(id: string | null) {
  simStore.selectedVehicleId = id;
  if (followingId.value) mapRef.value?.followVehicle(id);
}

function toggleFollow() {
  const id = simStore.selectedVehicleId;
  if (!id) return;
  mapRef.value?.followVehicle(followingId.value === id ? null : id);
}

function focusOrder(order: Fms.OrderDto) {
  if (!order.assignedVehicleId) return;
  simStore.selectedVehicleId = order.assignedVehicleId;
  if (followingId.value) mapRef.value?.followVehicle(order.assignedVehicleId);
  else mapRef.value?.focusVehicle(order.assignedVehicleId);
}

onMounted(async () => {
  await simStore.init();
  simStore.refreshOrders().catch(() => undefined);
  if (!mapStore.currentGraph) await mapStore.loadCurrentMap();
  try {
    slots.value = await fmsApi.getStorageSlots();
  } catch {
    slots.value = [];
  }
  clock = window.setInterval(() => {
    now.value = new Date();
  }, 1000);
});

onUnmounted(() => {
  if (clock) window.clearInterval(clock);
});
</script>

<template>
  <div class="flex h-[calc(100vh-108px)] min-h-0 flex-col overflow-hidden bg-[#f7f8fa] dark:bg-[#101418]">
    <header class="flex h-14 shrink-0 items-center gap-8 border-b border-slate-200/90 bg-white px-5 dark:border-slate-800 dark:bg-[#141a21]">
      <div class="min-w-[148px]">
        <div class="text-[15px] font-semibold tracking-tight text-slate-950 dark:text-slate-50">运行大屏</div>
        <div class="truncate text-[11px] text-slate-500">{{ mapStore.currentGraph?.name || '未载入地图' }}</div>
      </div>
      <div class="flex min-w-0 flex-1 items-end gap-6 overflow-auto">
        <div v-for="item in figures" :key="item.label" class="shrink-0 pb-0.5">
          <div class="text-[10px] tracking-wide text-slate-400">{{ item.label }}</div>
          <div
            class="mt-1 text-[22px] font-medium tabular-nums leading-none tracking-tight"
            :class="item.alert ? 'text-red-700 dark:text-red-300' : 'text-slate-950 dark:text-slate-50'"
          >
            {{ item.value }}
          </div>
        </div>
      </div>
      <NButton size="small" type="primary" :disabled="!hasAuth('Fms.Orders.Create')" :title="hasAuth('Fms.Orders.Create') ? undefined : '缺少权限 Fms.Orders.Create'" @click="showCreate = true">
        <template #icon><div class="i-carbon-add" /></template>
        新建任务
      </NButton>
      <time class="shrink-0 font-mono text-[12px] tabular-nums text-slate-500">{{ now.toLocaleTimeString() }}</time>
    </header>

    <div class="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_296px]">
      <MapCanvas
        ref="mapRef"
        :graph="mapStore.currentGraph"
        :vehicles="simStore.vehicles"
        :orders="liveOrders"
        :mutex-zones="simStore.mutexZones"
        :point-cloud="mapStore.pointCloud"
        :raster="mapStore.raster"
        :layer-visibility="mapStore.layerVisibility"
        :selected-vehicle-id="simStore.selectedVehicleId"
        @select-vehicle="pickVehicle"
        @follow-change="followingId = $event"
      />

      <aside class="flex min-h-0 flex-col border-l border-slate-200 bg-white dark:border-slate-800 dark:bg-[#141a21]">
        <div class="border-b border-slate-200 px-4 py-3 dark:border-slate-800">
          <div class="text-sm font-medium text-slate-900 dark:text-slate-100">在途任务</div>
          <div class="mt-0.5 text-xs text-slate-500">{{ liveOrders.length }} 条，{{ orderRunning }} 条执行中</div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto">
          <button
            v-for="order in liveOrders.slice(0, 30)"
            :key="order.orderId"
            type="button"
            class="block w-full border-b border-slate-100 px-4 py-2.5 text-left transition-colors hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-white/5"
            :class="order.assignedVehicleId && order.assignedVehicleId === simStore.selectedVehicleId ? 'bg-blue-50/70 dark:bg-blue-500/10' : ''"
            @click="focusOrder(order)"
          >
            <div class="flex items-baseline justify-between gap-3">
              <span class="truncate text-[13px] text-slate-900 dark:text-slate-100">{{ order.serial || order.orderId }}</span>
              <span class="shrink-0 text-[11px] text-slate-500">{{ orderStatusLabel(order.status) }}</span>
            </div>
            <div class="mt-0.5 truncate text-[11px] text-slate-500">
              <template v-if="order.sourceNodeId || order.targetNodeId">
                {{ nodeCaption(order.sourceNodeId, mapStore.currentGraph?.nodes) }}
                <template v-if="order.sourceNodeId"> → </template>
                {{ nodeCaption(order.targetNodeId, mapStore.currentGraph?.nodes) }}
                ·
              </template>
              {{ vehicleCaption(order.assignedVehicleId || order.bindVehicleId, simStore.vehicles) }}
              · {{ orderOriginLabel(order.origin) }}
            </div>
            <div class="mt-2 h-0.5 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800">
              <div class="h-full rounded-full transition-[width] duration-500" :class="orderTone(order.status)" :style="{ width: `${Math.max(percent(order), 3)}%` }" />
            </div>
            <div class="mt-1 flex justify-between gap-2 text-[10.5px] text-slate-400">
              <span class="truncate">{{ order.failReason || stepLabel(order.stepName) || '等待分配车辆' }}</span>
              <span class="shrink-0 tabular-nums">{{ order.currentStep ?? 0 }}/{{ order.totalSteps ?? 0 }}</span>
            </div>
          </button>
          <div v-if="liveOrders.length === 0" class="px-4 py-10 text-center text-xs text-slate-500">当前没有在途任务</div>

          <template v-if="recentOrders.length">
            <div class="px-4 pb-1 pt-4 text-[10.5px] tracking-wide text-slate-400">最近结束</div>
            <div
              v-for="order in recentOrders"
              :key="order.orderId"
              class="flex items-center gap-2 px-4 py-1.5 text-[12px] text-slate-500"
            >
              <span class="h-1.5 w-1.5 shrink-0 rounded-full" :class="orderTone(order.status)" />
              <span class="min-w-0 flex-1 truncate">{{ order.serial || order.orderId }}</span>
              <span class="shrink-0 text-[11px]">{{ orderStatusLabel(order.status) }}</span>
            </div>
          </template>
        </div>

        <div v-if="selectedVehicle" class="border-t border-slate-200 px-4 py-3 text-[12px] dark:border-slate-800">
          <div class="flex items-center justify-between">
            <span class="font-medium text-slate-900 dark:text-slate-100">{{ vehicleCaption(selectedVehicle.id, simStore.vehicles) }}</span>
            <button
              type="button"
              class="text-[11px] text-slate-400 hover:text-slate-700 dark:hover:text-slate-200"
              @click="
                mapRef?.followVehicle(null);
                simStore.selectedVehicleId = null;
              "
            >
              取消选择
            </button>
          </div>
          <div class="mt-1 text-slate-500">
            {{ vehicleStatusLabel(selectedVehicle.status) }} · 电量 {{ Math.round(selectedVehicle.batteryPercent) }}% ·
            {{ selectedVehicle.currentSpeed.toFixed(1) }} m/s
          </div>
          <div class="mt-0.5 text-slate-500">
            {{ selectedOrder ? `执行 ${selectedOrder.serial}` : '当前没有任务' }}
          </div>
          <div v-if="blockerText" class="mt-0.5 text-amber-700 dark:text-amber-300">{{ blockerText }}</div>
          <div class="mt-2 flex gap-2">
            <NButton size="tiny" secondary @click="mapRef?.focusVehicle(selectedVehicle.id)">定位</NButton>
            <NButton size="tiny" :type="followingId === selectedVehicle.id ? 'primary' : 'default'" secondary @click="toggleFollow">
              {{ followingId === selectedVehicle.id ? '取消跟随' : '跟随' }}
            </NButton>
          </div>
        </div>

        <div class="space-y-1 border-t border-slate-200 px-4 py-3 text-[11px] leading-5 text-slate-500 dark:border-slate-800">
          <div>站点 {{ mapStore.stats.totalNodes }} · 车道 {{ mapStore.stats.totalPaths }}</div>
          <div>路网 {{ mapStore.stats.totalLength }} m · 互斥区 {{ mapStore.stats.zones }}</div>
          <div>货位 {{ occupiedSlots }}/{{ slots.length }} · 点云 {{ mapStore.stats.pointCloud.toLocaleString() }}</div>
        </div>
      </aside>
    </div>

    <TaskCreateDialog v-model:show="showCreate" :vehicle-id="simStore.selectedVehicleId" />
  </div>
</template>
