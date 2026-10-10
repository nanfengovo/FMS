<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { authFetch } from '@/service/api/fms';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';
import { vehicleCaption, vehicleStatusLabel } from '@/utils/fms-display';

type OrderKind = 'Transport' | 'Nav' | 'Charge';

const props = withDefaults(
  defineProps<{
    show: boolean;
    kind?: OrderKind;
    /** 预填终点（例如在大屏上点选了某个站点） */
    targetNodeId?: string | null;
    vehicleId?: string | null;
  }>(),
  { kind: 'Transport', targetNodeId: null, vehicleId: null }
);

const emit = defineEmits<{
  (e: 'update:show', value: boolean): void;
  (e: 'created', payload: { orderId: string; serial: string }): void;
}>();

const { checkPermission } = useAuth();
const mapStore = useMapStore();
const simStore = useSimulationStore();

const submitting = ref(false);
const errorText = ref('');

const form = reactive({
  kind: 'Transport' as OrderKind,
  serial: '',
  vehicleId: '',
  sourceNodeId: '',
  targetNodeId: '',
  cargoId: '',
  priority: 60
});

const nodeTypeLabels: Record<string, string> = {
  ChargePoint: '充电点',
  StandbyPoint: '待命点',
  JobPoint: '作业工位',
  PullOverPoint: '避让点',
  Landmark: '地标'
};

const nodes = computed(() => mapStore.currentGraph?.nodes || []);

function toOptions(list: Fms.MapNodeDto[]) {
  return list.map(node => ({
    label: `${node.name && node.name !== node.id ? `${node.name}  ·  ` : ''}${node.id}  ·  ${nodeTypeLabels[node.type] || '站点'}`,
    value: node.id
  }));
}

/** 取放货工位优先排前面：作业工位、待命点，再到普通地标 */
const stationOptions = computed(() => {
  const rank = (type: string) => (type === 'JobPoint' ? 0 : type === 'StandbyPoint' ? 1 : type === 'PullOverPoint' ? 2 : type === 'ChargePoint' ? 3 : 4);
  return toOptions([...nodes.value].sort((a, b) => rank(a.type) - rank(b.type)));
});

const chargeOptions = computed(() => toOptions(nodes.value.filter(node => node.type === 'ChargePoint')));

const targetOptions = computed(() => (form.kind === 'Charge' ? chargeOptions.value : stationOptions.value));

const vehicleOptions = computed(() => [
  { label: '自动匹配最近的空闲车', value: '' },
  ...simStore.vehicles.map(vehicle => ({
    label: `${vehicleCaption(vehicle.id, simStore.vehicles)}  ·  ${vehicleStatusLabel(vehicle.status)}  ·  电量 ${Math.round(vehicle.batteryPercent)}%`,
    value: vehicle.id
  }))
]);

const kindOptions: Array<{ label: string; value: OrderKind; hint: string }> = [
  { label: '搬运', value: 'Transport', hint: '到起点取货，送到终点放货' },
  { label: '导航', value: 'Nav', hint: '车辆直接开到指定站点' },
  { label: '充电', value: 'Charge', hint: '开到充电点并充到 85%' }
];

const kindHint = computed(() => kindOptions.find(item => item.value === form.kind)?.hint || '');

function pickDefaults() {
  const standby = nodes.value.filter(node => node.type === 'StandbyPoint');
  const jobs = nodes.value.filter(node => node.type === 'JobPoint');
  const pool = jobs.length >= 2 ? jobs : standby.length >= 2 ? standby : nodes.value;
  if (!form.sourceNodeId && pool.length > 0) form.sourceNodeId = pool[0].id;
  if (!form.targetNodeId) {
    const other = pool.find(node => node.id !== form.sourceNodeId) || nodes.value.find(node => node.id !== form.sourceNodeId);
    form.targetNodeId = other?.id || '';
  }
  if (form.kind === 'Charge') {
    const charge = nodes.value.find(node => node.type === 'ChargePoint');
    form.targetNodeId = charge?.id || form.targetNodeId;
  }
}

watch(
  () => props.show,
  async visible => {
    if (!visible) return;
    errorText.value = '';
    if (!mapStore.currentGraph) await mapStore.loadCurrentMap();
    form.kind = props.kind;
    form.serial = `INT-${Date.now().toString().slice(-6)}`;
    form.vehicleId = props.vehicleId || '';
    form.cargoId = '';
    form.priority = 60;
    form.sourceNodeId = '';
    form.targetNodeId = props.targetNodeId || '';
    pickDefaults();
  },
  { immediate: true }
);

watch(
  () => form.kind,
  kind => {
    if (kind === 'Charge') {
      const charge = nodes.value.find(node => node.type === 'ChargePoint');
      if (charge) form.targetNodeId = charge.id;
    } else if (nodes.value.find(node => node.id === form.targetNodeId)?.type === 'ChargePoint') {
      form.targetNodeId = '';
      pickDefaults();
    }
  }
);

const canSubmit = computed(() => {
  if (!form.targetNodeId) return false;
  if (form.kind === 'Transport' && (!form.sourceNodeId || form.sourceNodeId === form.targetNodeId)) return false;
  return true;
});

const blockedReason = computed(() => {
  if (form.kind === 'Transport' && form.sourceNodeId && form.sourceNodeId === form.targetNodeId) return '起点和终点不能是同一个站点';
  return '';
});

async function submit() {
  if (!checkPermission('Fms.Orders.Create', '创建任务')) return;
  if (!canSubmit.value || submitting.value) return;
  submitting.value = true;
  errorText.value = '';
  try {
    const res = await authFetch('/api/fms/simulation/orders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        orderType: form.kind,
        taskSerial: form.serial || null,
        priority: form.priority,
        vehicleId: form.vehicleId || null,
        sourceNodeId: form.kind === 'Transport' ? form.sourceNodeId : null,
        targetNodeId: form.targetNodeId,
        cargoId: form.kind === 'Transport' ? form.cargoId || null : null
      })
    });
    const data = await res.json().catch(() => ({}));
    if (res.ok && data.success) {
      window.$message?.success(`任务 ${data.orderSerial} 已下发`);
      emit('created', { orderId: data.orderId, serial: data.orderSerial });
      emit('update:show', false);
      simStore.refreshOrders().catch(() => undefined);
    } else {
      errorText.value = data.error || data.message || `下发失败（${res.status}）`;
    }
  } catch (error: any) {
    errorText.value = error?.message || '网络异常，请稍后重试';
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    title="新建任务"
    style="width: 520px"
    :mask-closable="!submitting"
    @update:show="value => emit('update:show', value)"
  >
    <div class="mb-4 flex gap-1 rounded-md bg-slate-100 p-1 dark:bg-white/5">
      <button
        v-for="item in kindOptions"
        :key="item.value"
        type="button"
        class="h-8 flex-1 rounded text-13px font-medium transition-colors"
        :class="
          form.kind === item.value
            ? 'bg-white text-slate-900 shadow-sm dark:bg-[#1c2430] dark:text-white'
            : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'
        "
        @click="form.kind = item.value"
      >
        {{ item.label }}
      </button>
    </div>
    <p class="mb-4 text-12px text-slate-500">{{ kindHint }}</p>

    <NForm label-placement="left" label-width="84" size="small">
      <NFormItem label="任务编号">
        <NInput v-model:value="form.serial" placeholder="留空自动生成" />
      </NFormItem>

      <NFormItem label="执行车辆">
        <NSelect v-model:value="form.vehicleId" :options="vehicleOptions" filterable />
      </NFormItem>

      <NFormItem v-if="form.kind === 'Transport'" label="取货起点">
        <NSelect v-model:value="form.sourceNodeId" :options="stationOptions" filterable placeholder="选择站点" />
      </NFormItem>

      <NFormItem :label="form.kind === 'Charge' ? '充电点' : form.kind === 'Nav' ? '目标站点' : '放货终点'">
        <NSelect v-model:value="form.targetNodeId" :options="targetOptions" filterable placeholder="选择站点" />
      </NFormItem>

      <NFormItem v-if="form.kind === 'Transport'" label="载具编码">
        <NInput v-model:value="form.cargoId" placeholder="可选，例如 FOUP-8801" />
      </NFormItem>

      <NFormItem label="优先级">
        <div class="flex w-full items-center gap-3">
          <NSlider v-model:value="form.priority" :min="1" :max="100" class="flex-1" />
          <span class="w-8 text-right font-mono text-xs font-semibold">{{ form.priority }}</span>
        </div>
      </NFormItem>
    </NForm>

    <div v-if="blockedReason || errorText" class="mt-1 rounded-md bg-red-50 px-3 py-2 text-12px text-red-700 dark:bg-red-500/10 dark:text-red-300">
      {{ blockedReason || errorText }}
    </div>

    <template #footer>
      <div class="flex justify-end gap-2">
        <NButton size="small" :disabled="submitting" @click="emit('update:show', false)">取消</NButton>
        <NButton type="primary" size="small" :loading="submitting" :disabled="!canSubmit" @click="submit">下发任务</NButton>
      </div>
    </template>
  </NModal>
</template>
