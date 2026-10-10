<script setup lang="ts">
import { computed, ref } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { authFetch } from '@/service/api/fms';

const props = defineProps<{
  show: boolean;
  order: any | null;
}>();

const emit = defineEmits<{
  (e: 'update:show', value: boolean): void;
  (e: 'openStepModal', order: any): void;
  (e: 'refresh'): void;
}>();

const { hasAuth, checkPermission } = useAuth();
const isMaximized = ref(false);

function closeModal() {
  emit('update:show', false);
}

function toggleMaximize() {
  isMaximized.value = !isMaximized.value;
}

function handleOpenSteps() {
  if (props.order) {
    emit('openStepModal', props.order);
  }
}

async function handleSkipStep() {
  if (!props.order) return;
  if (!checkPermission('Fms.Orders.Skip', '手动跳步执行')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${props.order.id}/skip-step`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(`已强行跳至下一工序 (${data.currentStep + 1}/${data.totalSteps})`);
      emit('refresh');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleTogglePause() {
  if (!props.order) return;
  if (!checkPermission('Fms.Orders.Pause', '暂停/恢复任务')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${props.order.id}/toggle-pause`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.info(data.isPaused ? '任务已暂停' : '任务已恢复');
      emit('refresh');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleCancelOrder() {
  if (!props.order) return;
  if (!checkPermission('Fms.Orders.Cancel', '取消任务')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${props.order.id}/cancel`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(data.msg || '任务已成功取消');
      emit('refresh');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

// 20+ industrial parameters matching Images 2 & 3
const detailRows = computed(() => {
  if (!props.order) return [];
  const o = props.order;
  const ongoingStep = o.detailedSteps?.find((s: any) => s.status === 'Ongoing to Run');
  const currentStepName = ongoingStep?.name || (o.currentStepIndex < o.totalSteps ? (o.detailedSteps?.[o.currentStepIndex]?.name || 'MOVE_TO_POINT_target') : 'TASK_FINISH');

  return [
    { name: 'Current step (当前步骤)', value: currentStepName, editable: false },
    { name: 'Type of mission (任务类型)', value: o.taskType || 'Task', editable: false },
    { name: 'Priority (优先级)', value: `P${o.priority ?? 0}`, editable: false },
    { name: 'Finishing Action (完工策略)', value: o.finishingAction || 'Parking or charging', editable: false },
    { name: 'Task movement time (行驶耗时)', value: `${o.walkCostSec ?? 0} s`, editable: false },
    { name: 'Task wait time (等待耗时)', value: '0 s', editable: false },
    { name: 'Task mileage (任务里程)', value: `${(o.mileageKm ?? 0.055362).toFixed(6)} km`, editable: false },
    { name: 'complete_time (完成时间)', value: o.completedAt || o.startedAt || '2026-10-04 11:20:00', editable: false },
    { name: '机械臂动作 (ARM_Action)', value: o.armAction ?? 1, editable: true },
    { name: 'RFID(RFID)', value: o.rfid || '#', editable: true },
    { name: 'SubNo (子任务号)', value: o.subNo || o.serial, editable: false },
    { name: 'Succession (继发序)', value: 0, editable: false },
    { name: 'TaskCodea(TaskCodea)', value: o.taskCodeA || '133121', editable: true },
    { name: 'TaskCodeb(TaskCodeb)', value: o.taskCodeB || '133635', editable: true },
    { name: 'Task VehicleNo (分配车号)', value: o.assignedVehicle ? `AMR-${o.assignedVehicle.padStart(3, '0')}` : '-1 (未分配)', editable: false },
    { name: 'cargo_serial(cargo_id)', value: o.cargoSerial || '1', editable: true },
    { name: 'goal_action(goal_action)', value: 1, editable: true },
    { name: 'mark(mark)', value: o.mark ?? 3, editable: true },
    { name: 'option_code(option_code)', value: o.optionCode || '131073,133635', editable: true },
    { name: '途经点 (pass_point)', value: o.passPoint || '1', editable: true },
    { name: '终点前途经点 (pass_point_to_goal)', value: o.passPointToGoal || '25', editable: true },
    { name: 'robot_type (机器人型式)', value: '99 (Omni AMR)', editable: false },
    { name: 'storage (库位节点)', value: o.storage || 'STK-01', editable: true },
    { name: 'target (目标节点)', value: o.target || 'WB-01', editable: true }
  ];
});
</script>

<template>
  <NModal
    :show="show"
    :mask-closable="true"
    @update:show="emit('update:show', $event)"
  >
    <div
      class="bg-white dark:bg-slate-900 rounded-lg shadow-2xl border border-slate-300 dark:border-slate-700 flex flex-col overflow-hidden transition-all duration-200"
      :style="{
        width: isMaximized ? '96vw' : '820px',
        maxHeight: isMaximized ? '94vh' : '85vh',
        height: isMaximized ? '94vh' : 'auto'
      }"
    >
      <!-- Title Bar (Industrial Style matching Images 2 & 3) -->
      <div class="h-10 bg-slate-200 dark:bg-slate-800 border-b border-slate-300 dark:border-slate-700 px-3 flex items-center justify-between select-none">
        <div class="flex items-center gap-2">
          <div class="i-carbon-document-tasks text-blue-600 dark:text-blue-400 text-base" />
          <span class="font-bold text-xs text-slate-800 dark:text-slate-100 font-mono tracking-wide">
            Task Details - {{ order?.serial || '任务详情' }}
          </span>
          <NTag v-if="order" size="tiny" :bordered="false" type="info" class="ml-1">
            {{ order.status }}
          </NTag>
        </div>

        <!-- Window Controls: Minimize, Maximize, Close -->
        <div class="flex items-center gap-1.5">
          <button
            class="w-6 h-6 rounded flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-slate-300 dark:hover:bg-slate-700 transition"
            title="还原/居中"
            @click="isMaximized = false"
          >
            <div class="i-carbon-subtract text-xs" />
          </button>
          <button
            class="w-6 h-6 rounded flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-slate-300 dark:hover:bg-slate-700 transition"
            :title="isMaximized ? '还原窗口' : '最大化窗口'"
            @click="toggleMaximize"
          >
            <div :class="isMaximized ? 'i-carbon-minimize' : 'i-carbon-maximize'" class="text-xs" />
          </button>
          <button
            class="w-6 h-6 rounded flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-red-500 hover:text-white transition"
            title="关闭"
            @click="closeModal"
          >
            <div class="i-carbon-close text-xs" />
          </button>
        </div>
      </div>

      <!-- Quick Action Toolbar -->
      <div class="p-2.5 bg-slate-100/70 dark:bg-slate-800/50 border-b border-slate-200 dark:border-slate-700 flex flex-wrap items-center justify-between gap-2">
        <div class="flex items-center gap-2">
          <NButton size="tiny" type="primary" secondary @click="handleOpenSteps">
            <template #icon><div class="i-carbon-list-bulleted" /></template>
            打开工步执行流程 (12 Steps)
          </NButton>
          <span class="text-[11px] text-gray-400 font-mono">
            所属车辆: {{ order?.assignedVehicle ? `AMR-${order.assignedVehicle}` : '自动分配中' }}
          </span>
        </div>

        <div class="flex items-center gap-1.5">
          <NButton
            v-if="order && (order.status === 'Assigned' || order.status === 'Running')"
            size="tiny"
            type="warning"
            ghost
            :disabled="!hasAuth('Fms.Orders.Skip')"
            @click="handleSkipStep"
          >
            跳步 (Skip)
          </NButton>
          <NButton
            v-if="order && (order.status === 'Assigned' || order.status === 'Running')"
            size="tiny"
            secondary
            :disabled="!hasAuth('Fms.Orders.Pause')"
            @click="handleTogglePause"
          >
            暂停/恢复
          </NButton>
          <NButton
            v-if="order && (order.status === 'Queued' || order.status === 'Assigned')"
            size="tiny"
            type="error"
            secondary
            :disabled="!hasAuth('Fms.Orders.Cancel')"
            @click="handleCancelOrder"
          >
            取消任务
          </NButton>
        </div>
      </div>

      <!-- Table Body (Strictly matching Images 2 & 3) -->
      <div class="flex-1 overflow-y-auto p-3">
        <table class="w-full text-left text-xs border-collapse table-fixed border border-slate-200 dark:border-slate-700">
          <colgroup>
            <col style="width: 320px;" />
            <col />
            <col style="width: 70px;" />
          </colgroup>
          <thead>
            <tr class="bg-slate-200 dark:bg-slate-800 text-slate-700 dark:text-slate-200 font-semibold border-b border-slate-300 dark:border-slate-700">
              <th class="py-2 px-3">Name</th>
              <th class="py-2 px-3">Value</th>
              <th class="py-2 px-3 text-center">Operation</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-200 dark:divide-slate-800 font-mono">
            <tr
              v-for="(row, idx) in detailRows"
              :key="idx"
              class="h-8 hover:bg-slate-50 dark:hover:bg-slate-800/60 transition"
              :class="idx % 2 === 0 ? 'bg-white dark:bg-slate-900' : 'bg-slate-50/50 dark:bg-slate-800/30'"
            >
              <td class="py-1.5 px-3 text-slate-600 dark:text-slate-300 font-medium">
                {{ row.name }}
              </td>
              <td class="py-1.5 px-3 text-slate-800 dark:text-slate-100 truncate">
                {{ row.value }}
              </td>
              <td class="py-1.5 px-3 text-center">
                <button
                  v-if="row.editable"
                  class="text-blue-500 hover:text-blue-700 dark:hover:text-blue-300 transition"
                  title="修改参数"
                >
                  <div class="i-carbon-edit text-xs" />
                </button>
                <span v-else class="text-slate-300 dark:text-slate-600">-</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Footer Bar -->
      <div class="h-10 bg-slate-100 dark:bg-slate-800 border-t border-slate-200 dark:border-slate-700 px-3 flex items-center justify-between text-xs text-gray-500">
        <span class="font-mono">Total Attributes: {{ detailRows.length }}</span>
        <div class="flex items-center gap-2">
          <NButton size="tiny" @click="closeModal">关闭</NButton>
        </div>
      </div>
    </div>
  </NModal>
</template>

<style scoped>
</style>
