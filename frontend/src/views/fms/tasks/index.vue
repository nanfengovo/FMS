<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { useSimulationStore } from '@/store/modules/simulation';
import { useMapStore } from '@/store/modules/map';

import TaskDetailsModal from './TaskDetailsModal.vue';
import TaskStepModal from './TaskStepModal.vue';
import TaskCreateDialog from '@/components/fms/TaskCreateDialog.vue';
import { authFetch } from '@/service/api/fms';
import { formatTimestamp, orderStatusLabel, orderTypeLabel, taskSourceLabel, vehicleCaption } from '@/utils/fms-display';

const { hasAuth, checkPermission, currentRoleInfo } = useAuth();
const simStore = useSimulationStore();
const mapStore = useMapStore();

interface StepDto {
  stepIndex: number;
  name: string;
  action: string;
  targetNodeId: string;
  optionCode: string;
  cargoId: string;
  goalAction: number;
  dwellSec: number;
  isCompleted: boolean;
  startedAt?: string;
  finishedAt?: string;
  isActionPermitted: boolean;
  isRfidVerified: boolean;
}

interface OrderDto {
  id: string;
  serial: string;
  bulkTaskType: string;
  taskType: string;
  priority: number;
  status: string;
  assignedVehicle: string | null;
  bindVehicleId: string | null;
  sourceNodeId?: string;
  targetNodeId?: string;
  currentStepIndex: number;
  totalSteps: number;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  walkCostSec: number;
  cargoCostSec: number;
  failReason: string | null;
  subNo?: string;
  taskCodeA?: string;
  taskCodeB?: string;
  finishingAction?: string;
  cargoSerial?: string;
  armAction?: number;
  rfid?: string;
  mark?: number;
  optionCode?: string;
  passPoint?: string;
  passPointToGoal?: string;
  storage?: string;
  target?: string;
  mileageKm?: number;
  steps: StepDto[];
  detailedSteps?: any[];
}

const orders = ref<OrderDto[]>([]);
const loading = ref(false);
const filterSource = ref<string>('ALL');
const filterStatus = ref<string>('ALL');
const searchSerial = ref<string>('');

// Modals (No Drawer)
const showDetailsModal = ref(false);
const showStepModal = ref(false);
const currentOrderDetail = ref<OrderDto | null>(null);

// Create Task Dialog
const showCreateModal = ref(false);
const createKind = ref<'Transport' | 'Nav' | 'Charge'>('Transport');

async function fetchOrders() {
  try {
    const res = await authFetch('/api/fms/simulation/orders');
    if (res.ok) {
      const data = await res.json();
      if (data.success && Array.isArray(data.data)) {
        orders.value = data.data;
        if (currentOrderDetail.value) {
          const updated = data.data.find((o: OrderDto) => o.id === currentOrderDetail.value?.id);
          if (updated) currentOrderDetail.value = updated;
        }
      }
    }
  } catch (e) {
    console.error('Fetch orders error:', e);
  }
}

let timer: any = null;
onMounted(async () => {
  loading.value = true;
  await mapStore.loadCurrentMap();
  await fetchOrders();
  loading.value = false;
  timer = setInterval(fetchOrders, 2000);
});

onUnmounted(() => {
  if (timer) clearInterval(timer);
});

const filteredOrders = computed(() => {
  return orders.value.filter(o => {
    const matchSearch = !searchSerial.value || o.serial.toLowerCase().includes(searchSerial.value.toLowerCase());
    const matchSource = filterSource.value === 'ALL' || matchTaskSource(o, filterSource.value);
    const matchStatus = filterStatus.value === 'ALL' || o.status.toLowerCase() === filterStatus.value.toLowerCase();
    return matchSearch && matchSource && matchStatus;
  });
});

const page = ref(1);
const pageSize = ref(8);

watch([searchSerial, filterSource, filterStatus], () => {
  page.value = 1;
});

const paginatedOrders = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return filteredOrders.value.slice(start, start + pageSize.value);
});

const stats = computed(() => {
  const todayOrders = orders.value.filter(o => isSameLocalDay(o.createdAt));
  const total = todayOrders.length;
  const running = orders.value.filter(o => ['assigned', 'running', 'executing', 'waitingaction'].includes(o.status.toLowerCase())).length;
  const queued = orders.value.filter(o => o.status.toLowerCase() === 'queued').length;
  const done = orders.value.filter(o => ['done', 'completed'].includes(o.status.toLowerCase())).length;
  const failed = orders.value.filter(o => ['failed', 'cancelled', 'canceled'].includes(o.status.toLowerCase())).length;
  const completion = orders.value.length === 0 ? '—' : `${Math.round((done / orders.value.length) * 1000) / 10}%`;
  return { total, running, queued, done, failed, completion };
});

function isSameLocalDay(iso?: string) {
  if (!iso) return false;
  const raw = String(iso);
  const parsed = Date.parse(/Z|[+-]\d{2}:\d{2}$/.test(raw) ? raw : `${raw}Z`);
  if (Number.isNaN(parsed)) return false;
  const d = new Date(parsed);
  const now = new Date();
  return d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate();
}

function matchTaskSource(order: OrderDto, source: string) {
  const bulk = String(order.bulkTaskType || '').toLowerCase();
  const serial = String(order.serial || '').toUpperCase();
  const isExternal = bulk === 'task' || serial.startsWith('EXT-');
  if (source === 'MES') return bulk.includes('mes') || serial.includes('MES');
  if (source === 'WMS') return bulk.includes('wms') || serial.includes('WMS');
  if (source === 'INTERNAL') return bulk === 'internal';
  if (source === 'EXTERNAL') return isExternal;
  if (source === 'SYSTEM') return bulk.includes('auto');
  return true;
}

function openDetail(order: OrderDto) {
  currentOrderDetail.value = order;
  showDetailsModal.value = true;
}

function openSteps(order: OrderDto) {
  currentOrderDetail.value = order;
  showStepModal.value = true;
}

function openCreateModal(tab: 'transport' | 'nav' | 'charge' = 'transport') {
  if (!checkPermission('Fms.Orders.Create', '创建新任务')) return;
  createKind.value = tab === 'nav' ? 'Nav' : tab === 'charge' ? 'Charge' : 'Transport';
  showCreateModal.value = true;
}

async function handleCancelOrder(id: string) {
  if (!checkPermission('Fms.Orders.Cancel', '取消任务')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${id}/cancel`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(data.msg || '任务已成功取消');
      await fetchOrders();
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleTogglePause(id: string) {
  if (!checkPermission('Fms.Orders.Pause', '暂停/恢复任务')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${id}/toggle-pause`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.info(data.isPaused ? '任务已暂停' : '任务已恢复');
      await fetchOrders();
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleSkipStep(id: string) {
  if (!checkPermission('Fms.Orders.Skip', '手动跳步执行')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${id}/skip-step`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(`已强行步进至下一工序 (${data.currentStep + 1}/${data.totalSteps})`);
      await fetchOrders();
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

function getStatusTagType(status: string) {
  switch (status.toLowerCase()) {
    case 'done':
      return 'success';
    case 'assigned':
    case 'running':
      return 'info';
    case 'queued':
    case 'waitingaction':
    case 'paused':
      return 'warning';
    case 'cancelled':
    case 'failed':
      return 'error';
    default:
      return 'default';
  }
}

function getStatusLabel(status: string) {
  return orderStatusLabel(status);
}

function isOrderPaused(order: OrderDto) {
  return simStore.vehicles.find(vehicle => vehicle.id === order.assignedVehicle)?.status === 'Paused';
}

function isOpenOrder(status: string) {
  return !['done', 'completed', 'failed', 'canceled', 'cancelled', 'terminated'].includes(status.toLowerCase());
}

function stepProgress(order: OrderDto) {
  const total = order.totalSteps || 0;
  if (total <= 0) return 0;
  const status = order.status.toLowerCase();
  if (status === 'done' || status === 'completed') return 100;
  return Math.min(99, Math.round(((order.currentStepIndex + 0.5) / total) * 100));
}
</script>

<template>
  <div class="p-4 pb-12 mx-auto space-y-4 w-full">
    <!-- Top KPI cards -->
    <div class="grid grid-cols-2 gap-4 md:grid-cols-5">
      <NCard size="small" class="shadow-sm">
        <NStatistic label="今日任务总数" :value="stats.total">
          <template #prefix>
            <div class="i-carbon-task text-primary text-xl" />
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" class="shadow-sm">
        <NStatistic label="正在执行" :value="stats.running">
          <template #prefix>
            <div class="i-carbon-in-progress text-blue-500 text-xl" />
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" class="shadow-sm">
        <NStatistic label="排队待命" :value="stats.queued">
          <template #prefix>
            <div class="i-carbon-hourglass text-amber-500 text-xl" />
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" class="shadow-sm">
        <NStatistic label="成功交付" :value="stats.done">
          <template #prefix>
            <div class="i-carbon-checkmark-filled text-emerald-500 text-xl" />
          </template>
        </NStatistic>
      </NCard>
      <NCard size="small" class="shadow-sm">
        <NStatistic label="完成率" :value="stats.completion">
          <template #prefix>
            <div class="i-carbon-meter-alt text-emerald-600 text-xl" />
          </template>
        </NStatistic>
      </NCard>
    </div>

    <!-- Main Card -->
    <NCard title="任务中心" size="medium" class="shadow-sm">
      <template #header-extra>
        <NSpace align="center" :size="8">
          <NTag size="small" round :bordered="false" type="info">当前角色: {{ currentRoleInfo.label }}</NTag>
          <NButton type="primary" size="small" :disabled="!hasAuth('Fms.Orders.Create')" :title="hasAuth('Fms.Orders.Create') ? undefined : '缺少权限 Fms.Orders.Create'" @click="openCreateModal('transport')">
            <template #icon><div class="i-carbon-add" /></template>
            创建点到点搬运
          </NButton>
          <NButton type="info" ghost size="small" :disabled="!hasAuth('Fms.Orders.Create')" :title="hasAuth('Fms.Orders.Create') ? undefined : '缺少权限 Fms.Orders.Create'" @click="openCreateModal('nav')">
            <template #icon><div class="i-carbon-direction-straight" /></template>
            单点导航
          </NButton>
          <NButton type="warning" ghost size="small" :disabled="!hasAuth('Fms.Orders.Create')" :title="hasAuth('Fms.Orders.Create') ? undefined : '缺少权限 Fms.Orders.Create'" @click="openCreateModal('charge')">
            <template #icon><div class="i-carbon-flash" /></template>
            指定回充
          </NButton>
        </NSpace>
      </template>

      <!-- Filter Controls -->
      <div class="flex flex-wrap items-center justify-between gap-3 mb-4 p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-100 dark:border-slate-800">
        <NSpace align="center" :size="12">
          <NInput
            v-model:value="searchSerial"
            placeholder="搜索任务流水号 (如 EXT-*)"
            clearable
            size="small"
            :style="{ width: '240px' }"
          >
            <template #prefix><div class="i-carbon-search text-gray-400" /></template>
          </NInput>

          <NSelect
            v-model:value="filterSource"
            size="small"
            :style="{ width: '160px' }"
            :options="[
              { label: '全部来源', value: 'ALL' },
              { label: 'MES 生产系统', value: 'MES' },
              { label: 'WMS 仓储系统', value: 'WMS' },
              { label: '内部手动创建', value: 'INTERNAL' },
              { label: '外部系统接口', value: 'EXTERNAL' },
              { label: '系统自动决策', value: 'SYSTEM' }
            ]"
          />

          <NSelect
            v-model:value="filterStatus"
            size="small"
            :style="{ width: '130px' }"
            :options="[
              { label: '全部状态', value: 'ALL' },
              { label: '排队中', value: 'Queued' },
              { label: '执行中', value: 'Assigned' },
              { label: '行驶中', value: 'Running' },
              { label: '等待动作', value: 'WaitingAction' },
              { label: '已完结', value: 'Done' },
              { label: '已取消', value: 'Cancelled' }
            ]"
          />
        </NSpace>

        <div class="flex items-center gap-3">
          <div class="text-xs text-slate-500 dark:text-slate-400 font-mono">
            已筛选 <strong class="text-blue-600 dark:text-blue-400 font-bold">{{ filteredOrders.length }}</strong> 笔任务
          </div>
          <NButton size="small" @click="fetchOrders">
            <template #icon><div class="i-carbon-renew" /></template>
            刷新队列
          </NButton>
        </div>
      </div>

      <!-- Task Table Container with Consistent Fixed Layout -->
      <div class="border border-slate-200 dark:border-slate-800 rounded overflow-hidden">
        <div class="overflow-x-auto">
          <table class="w-full text-left text-xs border-collapse table-fixed">
            <colgroup>
              <col style="width: 210px;" />
              <col style="width: 120px;" />
              <col style="width: 75px;" />
              <col style="width: 150px;" />
              <col style="width: 165px;" />
              <col style="width: 85px;" />
              <col style="width: 150px;" />
              <col style="width: 250px;" />
            </colgroup>
            <thead>
              <tr class="bg-slate-100 dark:bg-slate-800/80 text-slate-700 dark:text-slate-300 font-medium border-b border-slate-200 dark:border-slate-700">
                <th class="py-2.5 px-3">任务编号 / 来源</th>
                <th class="py-2.5 px-3">类型</th>
                <th class="py-2.5 px-3">优先级</th>
                <th class="py-2.5 px-3">分配车辆</th>
                <th class="py-2.5 px-3">执行进度</th>
                <th class="py-2.5 px-3">状态</th>
                <th class="py-2.5 px-3">创建时间</th>
                <th class="py-2.5 px-3 text-right">操作</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
              <tr
                v-for="order in paginatedOrders"
                :key="order.id"
                class="h-12 hover:bg-slate-50 dark:hover:bg-slate-800/50 transition cursor-pointer"
                @click="openDetail(order)"
              >
                <td class="py-2 px-3 truncate">
                  <div class="font-mono font-semibold text-slate-800 dark:text-slate-200 truncate">{{ order.serial }}</div>
                  <div class="truncate text-[10px] text-slate-400">
                    {{ taskSourceLabel(order.bulkTaskType) }}
                    <template v-if="order.targetNodeId">
                      · {{ order.sourceNodeId ? `${order.sourceNodeId} → ` : '' }}{{ order.targetNodeId }}
                    </template>
                  </div>
                </td>
                <td class="py-2 px-3 truncate">
                  <NTag size="tiny" :bordered="false">{{ orderTypeLabel(order.taskType) }}</NTag>
                </td>
                <td class="py-2 px-3">
                  <NTag size="tiny" :type="order.priority >= 80 ? 'error' : order.priority >= 50 ? 'warning' : 'default'">
                    P{{ order.priority }}
                  </NTag>
                </td>
                <td class="py-2 px-3">
                  <span
                    v-if="order.assignedVehicle"
                    class="block truncate font-mono font-medium text-blue-600 dark:text-blue-400"
                    :title="vehicleCaption(order.assignedVehicle, simStore.vehicles)"
                  >
                    {{ vehicleCaption(order.assignedVehicle, simStore.vehicles) }}
                  </span>
                  <span v-else class="text-gray-400 italic">待空闲匹配</span>
                </td>
                <td class="py-2 px-3">
                  <div class="flex items-center gap-2">
                    <NProgress
                      type="line"
                      :percentage="stepProgress(order)"
                      :show-indicator="false"
                      :status="order.status.toLowerCase() === 'done' || order.status.toLowerCase() === 'completed' ? 'success' : 'default'"
                      style="width: 70px"
                    />
                    <span class="text-[11px] text-gray-500 whitespace-nowrap">
                      步 {{ Math.min(order.currentStepIndex + 1, order.totalSteps) }}/{{ order.totalSteps }}
                    </span>
                  </div>
                </td>
                <td class="py-2 px-3">
                  <NTag size="tiny" :type="getStatusTagType(order.status)">
                    {{ getStatusLabel(order.status) }}
                  </NTag>
                </td>
                <td class="py-2 px-3 text-gray-400 text-[11px] truncate">{{ formatTimestamp(order.createdAt) }}</td>
                <td class="py-2 px-3 text-right" @click.stop>
                  <div class="flex items-center justify-end gap-1.5 min-w-[220px]">
                    <NButton size="tiny" secondary type="primary" @click="openDetail(order)">
                      详情
                    </NButton>
                    <NButton size="tiny" secondary type="info" @click="openSteps(order)">
                      工步
                    </NButton>
                    <NButton
                      v-if="isOpenOrder(order.status) && order.assignedVehicle"
                      size="tiny"
                      secondary
                      :type="isOrderPaused(order) ? 'success' : 'default'"
                      :disabled="!hasAuth('Fms.Orders.Pause')"
                      :title="hasAuth('Fms.Orders.Pause') ? (isOrderPaused(order) ? '恢复车辆执行' : '暂停车辆执行') : '当前角色没有暂停权限'"
                      @click="handleTogglePause(order.id)"
                    >
                      {{ isOrderPaused(order) ? '恢复' : '暂停' }}
                    </NButton>
                    <NButton
                      v-if="isOpenOrder(order.status)"
                      size="tiny"
                      secondary
                      type="warning"
                      :disabled="!hasAuth('Fms.Orders.Skip')"
                      :title="hasAuth('Fms.Orders.Skip') ? '跳过当前工步' : '当前角色没有跳步权限'"
                      @click="handleSkipStep(order.id)"
                    >
                      跳步
                    </NButton>
                    <NPopconfirm
                      v-if="isOpenOrder(order.status)"
                      @positive-click="handleCancelOrder(order.id)"
                    >
                      <template #trigger>
                        <NButton
                          size="tiny"
                          secondary
                          type="error"
                          :disabled="!hasAuth('Fms.Orders.Cancel')"
                          :title="hasAuth('Fms.Orders.Cancel') ? '取消任务' : '当前角色没有取消权限'"
                        >
                          取消
                        </NButton>
                      </template>
                      确认强行中止此项调度任务？
                    </NPopconfirm>
                  </div>
                </td>
              </tr>
              <tr v-if="paginatedOrders.length === 0">
                <td colspan="8" class="py-12 text-center text-gray-400 text-xs">
                  <NEmpty description="暂无符合筛选条件的调度任务" size="small" />
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Table Pagination Footer -->
        <div class="flex flex-wrap items-center justify-between gap-3 p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-800/30">
          <div class="text-xs text-slate-500 dark:text-slate-400">
            共 <strong class="text-slate-800 dark:text-slate-200 font-semibold">{{ filteredOrders.length }}</strong> 条任务记录
            <span v-if="filteredOrders.length > 0" class="ml-1 text-gray-400">
              (第 {{ page }} / {{ Math.ceil(filteredOrders.length / pageSize) || 1 }} 页)
            </span>
          </div>
          <NPagination
            v-model:page="page"
            v-model:page-size="pageSize"
            :item-count="filteredOrders.length"
            :page-sizes="[5, 8, 10, 20]"
            show-size-picker
            show-quick-jumper
            size="small"
          />
        </div>
      </div>
    </NCard>

    <!-- Task Details Modal (No Drawer) -->
    <TaskDetailsModal
      v-model:show="showDetailsModal"
      :order="currentOrderDetail"
      @open-step-modal="openSteps"
      @refresh="fetchOrders"
    />

    <!-- Task Step Modal (No Drawer) -->
    <TaskStepModal
      v-model:show="showStepModal"
      :order="currentOrderDetail"
      @refresh="fetchOrders"
    />

    <TaskCreateDialog v-model:show="showCreateModal" :kind="createKind" @created="fetchOrders" />
  </div>
</template>
