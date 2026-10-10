<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { authFetch } from '@/service/api/fms';

const props = defineProps<{
  show: boolean;
  order: any | null;
}>();

const emit = defineEmits<{
  (e: 'update:show', value: boolean): void;
  (e: 'refresh'): void;
}>();

const { checkPermission } = useAuth();
const isMaximized = ref(false);
const actionLoading = ref(false);

function closeModal() {
  emit('update:show', false);
}

function toggleMaximize() {
  isMaximized.value = !isMaximized.value;
}

// Reset current step
async function handleResetCurrentStep() {
  if (!props.order) return;
  if (!checkPermission('Fms.Orders.Skip', '重置当前工步')) return;
  actionLoading.value = true;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${props.order.id}/steps/reset`, {
      method: 'POST'
    });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(data.msg || '当前工步已成功重置为 Waiting to Run');
      emit('refresh');
    } else {
      window.$message?.error(data.error || '工步重置失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  } finally {
    actionLoading.value = false;
  }
}

// Abort current step
async function handleAbortCurrentStep() {
  if (!props.order) return;
  if (!checkPermission('Fms.Orders.Cancel', '中止当前工步')) return;
  actionLoading.value = true;
  try {
    const res = await authFetch(`/api/fms/simulation/orders/${props.order.id}/steps/abort`, {
      method: 'POST'
    });
    const data = await res.json();
    if (data.success) {
      window.$message?.warning(data.msg || '当前工步已强制标记为 Aborted 中止');
      emit('refresh');
    } else {
      window.$message?.error(data.error || '工步中止失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  } finally {
    actionLoading.value = false;
  }
}

// 12 industrial standard steps
const steps = computed(() => {
  if (!props.order?.detailedSteps || props.order.detailedSteps.length === 0) {
    // Fallback standard 12 steps matching Image 1
    return [
      { no: 1, name: 'UPDATA_RFID_TO_AGV', type: 'MODIFY_VEHICLE_PARAM', status: 'Finish', parameter: 'value:RFID vehicle_param:RFID' },
      { no: 2, name: 'UPDATA_TaskCodea_TO_AGV', type: 'MODIFY_VEHICLE_PARAM', status: 'Finish', parameter: 'value:TaskCodea vehicle_param:TaskCodea' },
      { no: 3, name: 'UPDATA_TaskCodeb_TO_AGV', type: 'MODIFY_VEHICLE_PARAM', status: 'Finish', parameter: 'value:TaskCodeb vehicle_param:TaskCodeb' },
      { no: 4, name: 'REPORT_TASK_BEGIN', type: 'Custom_Step', status: 'Finish', parameter: 'AbortFunc: FinishFunc: GetChangeParamFunc:ReportTaskStart PauseFunc: ResumeFunc: RunningFunc: StartFunc' },
      { no: 5, name: 'TASK_BEGIN', type: 'SET_VAR', status: 'Finish', parameter: 'datatype:UINT name:ARVTask value:1' },
      { no: 6, name: 'MOVE_TO_POINT_target', type: 'MOVE', status: 'Ongoing to Run', parameter: 'goal_id:target pass_point:pass_point pass_point_to_goal:pass_point_to_goal' },
      { no: 7, name: 'REPORT_TASK_ARRIVE', type: 'Custom_Step', status: 'Waiting to Run', parameter: 'GetChangeParamFunc:ReportTaskArrive' },
      { no: 8, name: 'REPORT_PERMISS_ACTION', type: 'Custom_Step', status: 'Waiting to Run', parameter: 'GetChangeParamFunc:PermissAction' },
      { no: 9, name: 'ARMACTION_BEGIN', type: 'SET_VAR', status: 'Waiting to Run', parameter: 'datatype:UINT name:ARMAction value:1' },
      { no: 10, name: 'CHECK_ARMACTIONOK', type: 'CHECK_VAR', status: 'Waiting to Run', parameter: 'datatype:UINT expect_value:1 name:ARMActionOK' },
      { no: 11, name: 'REPORT_FINISH', type: 'Custom_Step', status: 'Waiting to Run', parameter: 'GetChangeParamFunc:ReportFinish' },
      { no: 12, name: 'TASK_FINISH', type: 'SET_VAR', status: 'Waiting to Run', parameter: 'datatype:UINT name:ARVTask value:0' }
    ];
  }
  return props.order.detailedSteps;
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
        width: isMaximized ? '96vw' : '960px',
        maxHeight: isMaximized ? '94vh' : '86vh',
        height: isMaximized ? '94vh' : 'auto'
      }"
    >
      <!-- Title Bar (Industrial SCADA Window Style matching Images 1 & 4) -->
      <div class="h-10 bg-slate-200 dark:bg-slate-800 border-b border-slate-300 dark:border-slate-700 px-3 flex items-center justify-between select-none">
        <div class="flex items-center gap-2">
          <div class="i-carbon-list-bulleted text-blue-600 dark:text-blue-400 text-base" />
          <span class="font-bold text-xs text-slate-800 dark:text-slate-100 font-mono tracking-wide">
            Task Step Details - {{ order?.serial || '工步执行详情' }}
          </span>
          <span class="text-[11px] text-gray-500 font-mono ml-2">
            [AGV: {{ order?.assignedVehicle ? `AMR-${order.assignedVehicle}` : 'N/A' }}]
          </span>
        </div>

        <!-- Window Controls: Minimize, Maximize, Close -->
        <div class="flex items-center gap-1.5">
          <button
            class="w-6 h-6 rounded flex items-center justify-center text-slate-600 dark:text-slate-300 hover:bg-slate-300 dark:hover:bg-slate-700 transition"
            title="居中"
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

      <!-- Action Button Toolbar (Strictly matching Images 1 & 4) -->
      <div class="p-2.5 bg-slate-100 dark:bg-slate-800/60 border-b border-slate-300 dark:border-slate-700 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <button
            class="px-3 py-1 bg-slate-200 hover:bg-slate-300 dark:bg-slate-700 dark:hover:bg-slate-600 text-slate-800 dark:text-slate-100 text-xs font-semibold rounded border border-slate-400 dark:border-slate-500 shadow-sm transition active:scale-95 disabled:opacity-50"
            :disabled="actionLoading"
            @click="handleResetCurrentStep"
          >
            Reset current step
          </button>

          <button
            class="px-3 py-1 bg-slate-200 hover:bg-red-100 hover:text-red-700 dark:bg-slate-700 dark:hover:bg-red-900/40 text-slate-800 dark:text-slate-100 text-xs font-semibold rounded border border-slate-400 dark:border-slate-500 shadow-sm transition active:scale-95 disabled:opacity-50"
            :disabled="actionLoading"
            @click="handleAbortCurrentStep"
          >
            Abort current step
          </button>
        </div>

        <div class="flex items-center gap-2 text-xs">
          <NButton size="tiny" secondary @click="emit('refresh')">
            <template #icon><div class="i-carbon-renew" /></template>
            刷新状态
          </NButton>
        </div>
      </div>

      <!-- Steps Table (Strictly matching Image 1: No. | Name | Type | Status | Parameter) -->
      <div class="flex-1 overflow-y-auto p-3">
        <table class="w-full text-left text-xs border-collapse table-fixed border border-slate-300 dark:border-slate-700">
          <colgroup>
            <col style="width: 50px;" />
            <col style="width: 220px;" />
            <col style="width: 170px;" />
            <col style="width: 130px;" />
            <col />
          </colgroup>
          <thead>
            <tr class="bg-slate-200 dark:bg-slate-800 text-slate-700 dark:text-slate-200 font-semibold border-b border-slate-300 dark:border-slate-700">
              <th class="py-2 px-3 text-center">No.</th>
              <th class="py-2 px-3">Name</th>
              <th class="py-2 px-3">Type</th>
              <th class="py-2 px-3">Status</th>
              <th class="py-2 px-3">Parameter</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-200 dark:divide-slate-800 font-mono">
            <tr
              v-for="step in steps"
              :key="step.no"
              class="h-8 hover:bg-blue-50/40 dark:hover:bg-slate-800/60 transition"
              :class="[
                step.status === 'Ongoing to Run'
                  ? 'bg-blue-50/70 dark:bg-blue-950/30 font-semibold'
                  : step.no % 2 === 0
                  ? 'bg-slate-50/40 dark:bg-slate-800/20'
                  : 'bg-white dark:bg-slate-900'
              ]"
            >
              <td class="py-1.5 px-3 text-center text-slate-500 dark:text-slate-400">
                {{ step.no }}
              </td>
              <td class="py-1.5 px-3 text-slate-800 dark:text-slate-200 font-medium truncate">
                {{ step.name }}
              </td>
              <td class="py-1.5 px-3 text-slate-600 dark:text-slate-400 truncate">
                {{ step.type }}
              </td>
              <td class="py-1.5 px-3">
                <!-- Status Icons & Badges matching Image 1 -->
                <div v-if="step.status === 'Finish'" class="flex items-center gap-1.5 text-emerald-600 dark:text-emerald-400 font-semibold">
                  <div class="i-carbon-checkmark-filled text-sm" />
                  <span>Finish</span>
                </div>
                <div v-else-if="step.status === 'Ongoing to Run'" class="flex items-center gap-1.5 text-blue-600 dark:text-blue-400 font-semibold">
                  <div class="i-carbon-play-filled text-sm animate-pulse" />
                  <span>Ongoing to Run</span>
                </div>
                <div v-else-if="step.status === 'Aborted'" class="flex items-center gap-1.5 text-red-600 dark:text-red-400 font-semibold">
                  <div class="i-carbon-close-filled text-sm" />
                  <span>Aborted</span>
                </div>
                <div v-else class="flex items-center gap-1.5 text-slate-500 dark:text-slate-400">
                  <div class="i-carbon-pause-outline text-sm" />
                  <span>Waiting to Run</span>
                </div>
              </td>
              <td class="py-1.5 px-3 text-slate-600 dark:text-slate-300 truncate" :title="step.parameter">
                {{ step.parameter }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Footer Bar -->
      <div class="h-10 bg-slate-100 dark:bg-slate-800 border-t border-slate-200 dark:border-slate-700 px-3 flex items-center justify-between text-xs text-gray-500">
        <span class="font-mono">Total Steps: {{ steps.length }}</span>
        <NButton size="tiny" @click="closeModal">关闭</NButton>
      </div>
    </div>
  </NModal>
</template>

<style scoped>
</style>
