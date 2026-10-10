<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { driveTypeLabel, formatTimestamp, nodeCaption, vehicleStatusLabel } from '@/utils/fms-display';
import { useSimulationStore } from '@/store/modules/simulation';
import { useMapStore } from '@/store/modules/map';
import { authFetch, fmsApi } from '@/service/api/fms';

const { hasAuth, checkPermission, currentRoleInfo } = useAuth();
const simStore = useSimulationStore();
const mapStore = useMapStore();
const canEditLedger = computed(() => hasAuth('Fms.Fleet.Teleop'));
const ledgerPermissionHint = '缺少权限 Fms.Fleet.Teleop';

const driveTypeOptions = [
  { label: '差速', value: 'Differential' },
  { label: '单舵轮', value: 'SingleSteer' },
  { label: '双舵轮', value: 'DualSteer' },
  { label: '全向', value: 'Mecanum' }
];

function vehicleTagType(status: string): 'default' | 'error' | 'info' | 'success' | 'warning' {
  switch (status.toLowerCase()) {
    case 'running':
      return 'info';
    case 'idle':
      return 'success';
    case 'charging':
    case 'paused':
    case 'avoiding':
    case 'docking':
      return 'warning';
    case 'error':
    case 'estop':
      return 'error';
    default:
      return 'default';
  }
}

// Selected vehicle for details / teleop
const selectedVehicleId = ref('');
const vehicleQuery = ref('');
const statusFilter = ref<'all' | 'online' | 'offline' | 'idle' | 'running' | 'charging'>('all');
const enabledFilter = ref<'all' | 'enabled' | 'disabled'>('all');
const batteryFilter = ref<'all' | 'low' | 'charging'>('all');
const savingLedger = ref(false);
const togglingEnable = ref(false);

const mapNodes = computed(() => mapStore.currentGraph?.nodes || []);

const parkNodeOptions = computed(() =>
  mapNodes.value
    .filter(node => node.type === 'StandbyPoint' || node.type === 'ChargePoint')
    .map(node => ({
      label: `${node.name || node.id}（${node.type === 'ChargePoint' ? '充电点' : '待命点'}）`,
      value: node.id
    }))
);

function displayStatus(vehicle: Fms.VehicleDto) {
  if (vehicle.isEnabled === false || vehicle.offlineReason === 'disabled') return 'offline';
  if (vehicle.presenceOnline === false || vehicle.offlineReason === 'heartbeat_timeout') return 'offline';
  return vehicle.status;
}

function presenceLabel(vehicle: Fms.VehicleDto) {
  if (vehicle.isEnabled === false || vehicle.offlineReason === 'disabled') return '停用离线';
  if (vehicle.offlineReason === 'heartbeat_timeout' || vehicle.presenceOnline === false) return '心跳超时离线';
  return '上线';
}

const visibleVehicles = computed(() => {
  const keyword = vehicleQuery.value.trim().toLowerCase();
  const statusKey = statusFilter.value;
  const enabledKey = enabledFilter.value;
  const batteryKey = batteryFilter.value;
  const lowThreshold = batteryStrategy.forcedThreshold;
  return simStore.vehicles.filter(vehicle => {
    if (keyword) {
      const hay = `${vehicle.name} ${vehicle.id} ${vehicle.code || ''} ${vehicleStatusLabel(vehicle.status)}`.toLowerCase();
      if (!hay.includes(keyword) && !vehicleStatusLabel(vehicle.status).includes(vehicleQuery.value.trim())) return false;
    }
    const enabled = vehicle.isEnabled !== false;
    if (enabledKey === 'enabled' && !enabled) return false;
    if (enabledKey === 'disabled' && enabled) return false;
    if (batteryKey === 'low' && vehicle.batteryPercent > lowThreshold) return false;
    if (batteryKey === 'charging' && !vehicle.isCharging && vehicle.status.toLowerCase() !== 'charging') return false;
    if (statusKey === 'online' && (vehicle.presenceOnline === false || !enabled)) return false;
    if (statusKey === 'offline' && vehicle.presenceOnline !== false && enabled) return false;
    if (statusKey === 'idle' && vehicle.status.toLowerCase() !== 'idle') return false;
    if (statusKey === 'running' && vehicle.status.toLowerCase() !== 'running') return false;
    if (statusKey === 'charging' && vehicle.status.toLowerCase() !== 'charging' && !vehicle.isCharging) return false;
    return true;
  });
});

// Battery Strategy Form
const batteryStrategy = reactive({
  forcedThreshold: 20,
  opportunityThreshold: 40,
  fullThreshold: 85,
  strategyMode: 'nearest'
});

// Auto-dock Strategy Form
const dockStrategy = reactive({
  idleTimeoutSec: 10,
  preferParkZone: ''
});

// Teleop Destination
const teleopTargetNode = ref('');

const teleopNodeOptions = computed(() =>
  mapNodes.value
    .filter(node => node.type === 'StandbyPoint' || node.type === 'ChargePoint' || node.type === 'PullOverPoint')
    .map(node => ({
      label: `${node.name || node.id}（${node.type === 'ChargePoint' ? '充电点' : '待命点'}）`,
      value: node.id
    }))
);

watch(
  teleopNodeOptions,
  opts => {
    if (!opts.length) return;
    if (!opts.some(item => item.value === teleopTargetNode.value)) {
      teleopTargetNode.value = opts[0].value;
    }
    if (!opts.some(item => item.value === dockStrategy.preferParkZone)) {
      dockStrategy.preferParkZone = opts.find(item => item.label.includes('待命'))?.value || opts[0].value;
    }
  },
  { immediate: true }
);

function vehicleDisplayName(vehicle?: Fms.VehicleDto | null) {
  return vehicle?.name || vehicle?.id || selectedVehicleId.value;
}

function stationDisplay(vehicle: Fms.VehicleDto) {
  if (vehicle.currentNodeId) return nodeCaption(vehicle.currentNodeId, mapNodes.value);
  if (vehicle.nearestNodeId) return `近 ${nodeCaption(vehicle.nearestNodeId, mapNodes.value)}`;
  return '未吸附';
}

const selectedVehicle = computed(() => {
  return simStore.vehicles.find(v => v.id === selectedVehicleId.value) || visibleVehicles.value[0] || simStore.vehicles[0];
});

const ledgerDraft = reactive({
  name: '',
  driveType: 'Differential',
  length: 1.2,
  width: 0.8,
  maxSpeed: 1.5,
  homeNodeId: null as string | null
});

watch(
  () => selectedVehicle.value?.id,
  () => {
    const vehicle = selectedVehicle.value;
    if (!vehicle) return;
    if (selectedVehicleId.value !== vehicle.id) selectedVehicleId.value = vehicle.id;
    ledgerDraft.name = vehicle.name || vehicle.id;
    ledgerDraft.driveType = vehicle.driveType || 'Differential';
    ledgerDraft.length = vehicle.length ?? 1.2;
    ledgerDraft.width = vehicle.width ?? 0.8;
    ledgerDraft.maxSpeed = vehicle.maxSpeed ?? 1.5;
    ledgerDraft.homeNodeId = vehicle.homeNodeId || null;
  },
  { immediate: true }
);

let timer: ReturnType<typeof setInterval> | null = null;
onMounted(async () => {
  await mapStore.loadCurrentMap();
  await simStore.pollSimulationState();
  if (!selectedVehicleId.value && simStore.vehicles[0]) selectedVehicleId.value = simStore.vehicles[0].id;
  timer = setInterval(() => {
    simStore.pollSimulationState();
  }, 1500);
});

onUnmounted(() => {
  if (timer) clearInterval(timer);
});

async function handleAutoChargePlan() {
  if (!checkPermission('Fms.Fleet.AutoCharge', '触发全局自动电量回充规划')) return;
  try {
    const res = await authFetch(`/api/fms/simulation/fleet/auto-charge?threshold=${batteryStrategy.forcedThreshold}`, {
      method: 'POST'
    });
    const data = await res.json();
    if (data.success) {
      if (data.lowBatteryCount > 0) {
        window.$message?.success(`自动电量规划成功！已检测到 ${data.lowBatteryCount} 台低电量车辆，已自动派发回充工序任务！`);
      } else {
        window.$message?.info('全车队电量健康，暂无低于阈值的回充需求');
      }
      await simStore.pollSimulationState();
    } else {
      window.$message?.error(data.error || '电量规划失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleAutoDockPlan() {
  if (!checkPermission('Fms.Fleet.AutoDock', '触发空闲车辆自动停靠待命')) return;
  try {
    const res = await authFetch('/api/fms/simulation/fleet/auto-dock', { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      if (data.dockedCount > 0) {
        window.$message?.success(`自动停靠规划成功！已为 ${data.dockedCount} 台空闲车辆分配专属待命位，释放主走廊！`);
      } else {
        window.$message?.info('车队车辆均在执行任务或已在待命位');
      }
      await simStore.pollSimulationState();
    } else {
      window.$message?.error(data.error || '自动停靠调度失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleTeleopCommand(cmd: string, val?: number) {
  if (!checkPermission('Fms.Fleet.Teleop', '手动操控车辆')) return;
  try {
    const res = await authFetch('/api/fms/simulation/fleet/teleop', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        vehicleId: selectedVehicleId.value,
        command: cmd,
        value: val,
        targetNodeId: cmd === 'move_to' ? teleopTargetNode.value : null
      })
    });
    const data = await res.json();
    if (data.success) {
      if (cmd === 'set_battery') {
        window.$message?.success(`车辆 ${vehicleDisplayName(selectedVehicle.value)} 电量已重设为: ${val}%`);
      } else if (cmd === 'estop') {
        window.$message?.error(`已向车辆 ${vehicleDisplayName(selectedVehicle.value)} 发送紧急制动急停！`);
      } else if (cmd === 'clear_estop') {
        window.$message?.success(`车辆 ${vehicleDisplayName(selectedVehicle.value)} 急停已解除自愈！`);
      } else if (cmd === 'move_to') {
        window.$message?.success(`已派发调度指令：${vehicleDisplayName(selectedVehicle.value)} 前往 ${teleopTargetNode.value}`);
      }
      await simStore.pollSimulationState();
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

async function handleSaveLedger() {
  if (!selectedVehicle.value) return;
  if (!checkPermission('Fms.Fleet.Teleop', '保存车辆台账')) return;
  savingLedger.value = true;
  try {
    const data = await fmsApi.updateFleetVehicle(selectedVehicle.value.id, {
      name: ledgerDraft.name,
      driveType: ledgerDraft.driveType,
      length: ledgerDraft.length,
      width: ledgerDraft.width,
      maxSpeed: ledgerDraft.maxSpeed,
      homeNodeId: ledgerDraft.homeNodeId || ''
    });
    if (data.success) {
      window.$message?.success('车辆台账已保存');
      await simStore.pollSimulationState();
    } else {
      window.$message?.error(data.error || '保存台账失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  } finally {
    savingLedger.value = false;
  }
}

async function handleToggleEnabled(enabled: boolean) {
  if (!selectedVehicle.value) return;
  if (!checkPermission('Fms.Fleet.Teleop', enabled ? '启用车辆' : '停用车辆')) return;
  togglingEnable.value = true;
  try {
    const data = await fmsApi.setFleetVehicleEnabled(selectedVehicle.value.id, enabled);
    if (data.success) {
      window.$message?.success(enabled ? '车辆已启用，可承接新单' : '车辆已停用，不再派发新单');
      await simStore.pollSimulationState();
    } else {
      window.$message?.error(data.error || '更新启用状态失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  } finally {
    togglingEnable.value = false;
  }
}

function headingDeg(vehicle?: Fms.VehicleDto | null) {
  if (!vehicle?.pose) return 0;
  return ((vehicle.pose.theta || 0) * 180) / Math.PI;
}
</script>

<template>
  <div class="w-full space-y-4 p-4 pb-8">
    <!-- Header -->
    <NCard title="车辆台账" size="medium" class="shadow-sm">
      <template #header-extra>
        <NSpace align="center" :size="8">
          <NTag size="small" round :bordered="false" type="info">当前角色: {{ currentRoleInfo.label }}</NTag>
          <NButton
            type="warning"
            size="small"
            :disabled="!hasAuth('Fms.Fleet.AutoCharge')"
            :title="hasAuth('Fms.Fleet.AutoCharge') ? '巡检回充' : '缺少权限 Fms.Fleet.AutoCharge'"
            @click="handleAutoChargePlan"
          >
            <template #icon><div class="i-carbon-flash" /></template>
            巡检回充
          </NButton>
          <NButton
            type="primary"
            size="small"
            :disabled="!hasAuth('Fms.Fleet.AutoDock')"
            :title="hasAuth('Fms.Fleet.AutoDock') ? '空闲停靠' : '缺少权限 Fms.Fleet.AutoDock'"
            @click="handleAutoDockPlan"
          >
            <template #icon><div class="i-carbon-car" /></template>
            空闲停靠
          </NButton>
        </NSpace>
      </template>

      <div class="mb-3 flex flex-wrap items-center gap-2">
        <NInput v-model:value="vehicleQuery" clearable placeholder="搜索车号、名称或状态" style="width: 220px" size="small" />
        <NSelect
          v-model:value="statusFilter"
          size="small"
          style="width: 120px"
          :options="[
            { label: '全部状态', value: 'all' },
            { label: '上线', value: 'online' },
            { label: '离线', value: 'offline' },
            { label: '待命', value: 'idle' },
            { label: '运行', value: 'running' },
            { label: '充电', value: 'charging' }
          ]"
        />
        <NSelect
          v-model:value="enabledFilter"
          size="small"
          style="width: 110px"
          :options="[
            { label: '全部启用', value: 'all' },
            { label: '已启用', value: 'enabled' },
            { label: '已停用', value: 'disabled' }
          ]"
        />
        <NSelect
          v-model:value="batteryFilter"
          size="small"
          style="width: 110px"
          :options="[
            { label: '全部电量', value: 'all' },
            { label: '低电', value: 'low' },
            { label: '充电中', value: 'charging' }
          ]"
        />
        <span class="ml-auto text-xs text-slate-500">{{ visibleVehicles.length }} / {{ simStore.vehicles.length }} 台</span>
      </div>

      <div class="mb-4 max-h-[280px] overflow-auto rounded border border-slate-200 dark:border-slate-800">
        <table class="w-full border-collapse text-left text-xs">
          <thead class="sticky top-0 z-1 bg-slate-50 text-slate-500 dark:bg-slate-900">
            <tr>
              <th class="px-2 py-1.5 font-medium">编号</th>
              <th class="px-2 py-1.5 font-medium">名称</th>
              <th class="px-2 py-1.5 font-medium">状态</th>
              <th class="px-2 py-1.5 font-medium">启用</th>
              <th class="px-2 py-1.5 font-medium">电量</th>
              <th class="px-2 py-1.5 font-medium">当前点</th>
              <th class="px-2 py-1.5 font-medium">目标点</th>
              <th class="px-2 py-1.5 font-medium">坐标</th>
              <th class="px-2 py-1.5 font-medium">任务</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="v in visibleVehicles"
              :key="v.id"
              class="cursor-pointer border-t border-slate-100 dark:border-slate-800"
              :class="selectedVehicleId === v.id ? 'bg-blue-50/70 dark:bg-blue-900/20' : 'hover:bg-slate-50 dark:hover:bg-slate-800/40'"
              @click="selectedVehicleId = v.id"
            >
              <td class="px-2 py-1.5 font-mono">{{ v.code || v.id }}</td>
              <td class="px-2 py-1.5 font-medium text-slate-800 dark:text-slate-200">{{ v.name }}</td>
              <td class="px-2 py-1.5">
                <NTag size="tiny" :type="vehicleTagType(displayStatus(v))">{{ vehicleStatusLabel(displayStatus(v)) }}</NTag>
              </td>
              <td class="px-2 py-1.5">
                <NTag size="tiny" :type="v.isEnabled === false ? 'default' : 'success'">
                  {{ v.isEnabled === false ? '停用' : '启用' }}
                </NTag>
              </td>
              <td class="px-2 py-1.5">
                <span :class="v.batteryPercent <= 20 ? 'text-red-500' : v.batteryPercent <= 40 ? 'text-amber-600' : 'text-emerald-600'">
                  {{ v.batteryPercent.toFixed(0) }}%
                </span>
                <span v-if="v.isCharging" class="ml-1 text-amber-600">充</span>
              </td>
              <td class="px-2 py-1.5 font-mono">{{ stationDisplay(v) }}</td>
              <td class="px-2 py-1.5 font-mono">{{ v.targetNodeId ? nodeCaption(v.targetNodeId, mapNodes) : '-' }}</td>
              <td class="px-2 py-1.5 font-mono">{{ (v.pose?.x ?? 0).toFixed(1) }}, {{ (v.pose?.y ?? 0).toFixed(1) }}</td>
              <td class="px-2 py-1.5 font-mono">{{ v.currentOrderId || '—' }}</td>
            </tr>
            <tr v-if="visibleVehicles.length === 0">
              <td class="px-2 py-6 text-center text-slate-400" colspan="9">没有符合筛选条件的车辆</td>
            </tr>
          </tbody>
        </table>
      </div>

      <NCard v-if="selectedVehicle" title="车辆详情" size="small" class="mb-4 shadow-sm">
        <template #header-extra>
          <NSpace :size="8">
            <NTag size="tiny" :type="vehicleTagType(displayStatus(selectedVehicle))">
              {{ vehicleStatusLabel(displayStatus(selectedVehicle)) }}
            </NTag>
            <NTag size="tiny" :type="selectedVehicle.isEnabled === false ? 'default' : 'success'">
              {{ presenceLabel(selectedVehicle) }}
            </NTag>
          </NSpace>
        </template>
        <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <div class="space-y-3 text-xs">
            <div class="grid grid-cols-2 gap-2">
              <div>编号 <span class="font-mono font-semibold text-slate-700 dark:text-slate-200">{{ selectedVehicle.code || selectedVehicle.id }}</span></div>
              <div>型号 <span class="font-semibold">{{ driveTypeLabel(selectedVehicle.driveType) }}</span></div>
              <div>所属地图 <span class="font-semibold">{{ selectedVehicle.mapName || selectedVehicle.mapId || mapStore.currentGraph?.name || '—' }}</span></div>
              <div>当前任务 <span class="font-mono">{{ selectedVehicle.currentOrderId || '—' }}</span></div>
              <div>当前点 <span class="font-mono">{{ stationDisplay(selectedVehicle) }}</span></div>
              <div>目标点 <span class="font-mono">{{ selectedVehicle.targetNodeId ? nodeCaption(selectedVehicle.targetNodeId, mapNodes) : '-' }}</span></div>
              <div>坐标 <span class="font-mono">{{ (selectedVehicle.pose?.x ?? 0).toFixed(2) }} m, {{ (selectedVehicle.pose?.y ?? 0).toFixed(2) }} m</span></div>
              <div>朝向 <span class="font-mono">{{ headingDeg(selectedVehicle).toFixed(1) }}°</span></div>
              <div>速度 <span class="font-mono">{{ (selectedVehicle.currentSpeed ?? 0).toFixed(2) }} m/s</span></div>
              <div>
                电量
                <span class="font-mono font-semibold">{{ selectedVehicle.batteryPercent.toFixed(1) }}%</span>
                <span v-if="selectedVehicle.isCharging" class="ml-1 text-amber-600">充电中</span>
                <span class="ml-1 text-slate-400">低电阈值 {{ selectedVehicle.lowBatteryThreshold ?? 20 }}%</span>
              </div>
              <div class="col-span-2">最近遥测 {{ formatTimestamp(selectedVehicle.lastTelemetryUtc || selectedVehicle.lastHeartbeatUtc) || '—' }}</div>
            </div>
            <div class="flex flex-wrap gap-2 pt-1">
              <NButton
                size="small"
                :disabled="!canEditLedger || selectedVehicle.isEnabled !== false"
                :title="canEditLedger ? '启用后可承接新单' : ledgerPermissionHint"
                :loading="togglingEnable"
                @click="handleToggleEnabled(true)"
              >
                启用
              </NButton>
              <NButton
                size="small"
                type="warning"
                :disabled="!canEditLedger || selectedVehicle.isEnabled === false"
                :title="canEditLedger ? '停用后不再派新单' : ledgerPermissionHint"
                :loading="togglingEnable"
                @click="handleToggleEnabled(false)"
              >
                停用
              </NButton>
            </div>
          </div>
          <div class="space-y-2 text-xs">
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">名称</span>
              <NInput v-model:value="ledgerDraft.name" size="small" :disabled="!canEditLedger" />
            </div>
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">型号</span>
              <NSelect v-model:value="ledgerDraft.driveType" size="small" class="flex-1" :options="driveTypeOptions" :disabled="!canEditLedger" />
            </div>
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">车长 m</span>
              <NInputNumber v-model:value="ledgerDraft.length" size="small" :min="0.3" :max="5" :step="0.1" class="flex-1" :disabled="!canEditLedger" />
            </div>
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">车宽 m</span>
              <NInputNumber v-model:value="ledgerDraft.width" size="small" :min="0.3" :max="5" :step="0.1" class="flex-1" :disabled="!canEditLedger" />
            </div>
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">最高速</span>
              <NInputNumber v-model:value="ledgerDraft.maxSpeed" size="small" :min="0.1" :max="3" :step="0.1" class="flex-1" :disabled="!canEditLedger" />
            </div>
            <div class="flex items-center justify-between gap-2">
              <span class="w-16 shrink-0 text-slate-500">停靠点</span>
              <NSelect
                v-model:value="ledgerDraft.homeNodeId"
                size="small"
                class="flex-1"
                clearable
                filterable
                :options="parkNodeOptions"
                :disabled="!canEditLedger"
                placeholder="从待命点/充电点选择"
              />
            </div>
            <div class="flex justify-end pt-1">
              <NButton
                size="small"
                type="primary"
                :disabled="!canEditLedger"
                :title="canEditLedger ? '保存台账' : ledgerPermissionHint"
                :loading="savingLedger"
                @click="handleSaveLedger"
              >
                保存台账
              </NButton>
            </div>
          </div>
        </div>
      </NCard>

      <!-- Detail Panels: Left (Strategies: Battery + Docking) + Right (Vehicle Teleop) -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <!-- Left: Autonomous Strategies Settings -->
        <div class="space-y-4">
          <!-- Battery Strategy Card -->
          <NCard title="自动回充" size="small" class="shadow-sm">
            <div class="space-y-3 text-xs">
              <div class="flex items-center justify-between">
                <span class="text-gray-600 dark:text-gray-400">强制回充阈值 (SOC &le; % 自动派单抢桩):</span>
                <NInputNumber v-model:value="batteryStrategy.forcedThreshold" size="small" :min="10" :max="30" style="width: 100px" />
              </div>

              <div class="flex items-center justify-between">
                <span class="text-gray-600 dark:text-gray-400">机会回充阈值 (空闲车 &le; % 自动补电):</span>
                <NInputNumber v-model:value="batteryStrategy.opportunityThreshold" size="small" :min="30" :max="60" style="width: 100px" />
              </div>

              <div class="flex items-center justify-between">
                <span class="text-gray-600 dark:text-gray-400">充电达标恢复就绪阈值 (%):</span>
                <NInputNumber v-model:value="batteryStrategy.fullThreshold" size="small" :min="70" :max="100" style="width: 100px" />
              </div>

              <div class="p-2.5 bg-amber-50/60 dark:bg-amber-950/20 text-amber-700 dark:text-amber-400 rounded text-[11px] leading-relaxed">
                当前规则：当车辆 SOC &le; {{ batteryStrategy.forcedThreshold }}% 时，调度系统自动挂起非紧急任务，就近寻路至当前地图充电点，执行接触式自动对接快充。
              </div>

              <div class="flex justify-end pt-1">
                <NButton
                  size="small"
                  type="warning"
                  secondary
                  :disabled="!hasAuth('Fms.Fleet.AutoCharge')"
                  :title="hasAuth('Fms.Fleet.AutoCharge') ? '立即执行电量巡检' : '缺少权限 Fms.Fleet.AutoCharge'"
                  @click="handleAutoChargePlan"
                >
                  立即执行电量巡检与派发回充
                </NButton>
              </div>
            </div>
          </NCard>

          <!-- Auto-Docking Strategy Card -->
          <NCard title="自动停靠" size="small" class="shadow-sm">
            <div class="space-y-3 text-xs">
              <div class="flex items-center justify-between">
                <span class="text-gray-600 dark:text-gray-400">空闲驻留超限触发阈值 (秒):</span>
                <NInputNumber v-model:value="dockStrategy.idleTimeoutSec" size="small" :min="5" :max="60" style="width: 100px" />
              </div>

              <div class="p-2.5 bg-blue-50/60 dark:bg-blue-950/20 text-blue-700 dark:text-blue-400 rounded text-[11px] leading-relaxed">
                主干道走廊防堵塞规则：车辆执行完毕且等待超 {{ dockStrategy.idleTimeoutSec }} 秒无新任务指派时，禁止停留在核心过道，调度引擎自动指引驶入当前地图待命点停靠。
              </div>

              <div class="flex justify-end pt-1">
                <NButton
                  size="small"
                  type="primary"
                  secondary
                  :disabled="!hasAuth('Fms.Fleet.AutoDock')"
                  :title="hasAuth('Fms.Fleet.AutoDock') ? '立即调度空闲车入位' : '缺少权限 Fms.Fleet.AutoDock'"
                  @click="handleAutoDockPlan"
                >
                  立即调度空闲车入位停靠
                </NButton>
              </div>
            </div>
          </NCard>
        </div>

        <!-- Right: Teleoperation & Direct Vehicle Commands -->
        <NCard title="单车操控" size="small" class="shadow-sm">
          <template #header-extra>
            <NSelect
              v-model:value="selectedVehicleId"
              size="tiny"
              style="width: 150px"
              placeholder="选择操控车辆"
              :options="simStore.vehicles.map(v => ({ label: `${v.name} (${v.batteryPercent?.toFixed(0) || 100}%)`, value: v.id }))"
            />
          </template>
          <div class="space-y-4 text-xs" v-if="selectedVehicle">
            <div class="flex items-center justify-between p-3 bg-slate-50 dark:bg-slate-800/60 rounded border border-slate-200 dark:border-slate-800">
              <div>
                <div class="font-bold text-sm text-slate-800 dark:text-slate-200">{{ selectedVehicle.name }} (ID: {{ selectedVehicle.id }})</div>
                <div class="text-[11px] text-gray-500 mt-0.5">当前位置: ({{ selectedVehicle.pose?.x?.toFixed(1) || 0 }}m, {{ selectedVehicle.pose?.y?.toFixed(1) || 0 }}m) | 状态: {{ vehicleStatusLabel(selectedVehicle.status) }}</div>
              </div>
              <NTag size="small" :type="selectedVehicle.batteryPercent <= 20 ? 'error' : 'success'">
                {{ selectedVehicle.batteryPercent?.toFixed(1) || 100 }}% SOC
              </NTag>
            </div>

            <!-- Battery Mock Simulation Buttons -->
            <div class="space-y-1.5">
              <label class="font-medium text-gray-700 dark:text-gray-300">电量模拟仿真注入:</label>
              <div class="flex gap-2">
                <NButton size="small" secondary type="error" class="flex-1" @click="handleTeleopCommand('set_battery', 15)">
                  设为低电量 (15%)
                </NButton>
                <NButton size="small" secondary type="warning" class="flex-1" @click="handleTeleopCommand('set_battery', 35)">
                  设为中电量 (35%)
                </NButton>
                <NButton size="small" secondary type="success" class="flex-1" @click="handleTeleopCommand('set_battery', 100)">
                  设为满电量 (100%)
                </NButton>
              </div>
            </div>

            <!-- Direct Dispatch to Node -->
            <div class="space-y-1.5">
              <label class="font-medium text-gray-700 dark:text-gray-300">一键点动指派工位导航:</label>
              <div class="flex gap-2">
                <NSelect
                  v-model:value="teleopTargetNode"
                  size="small"
                  class="flex-1"
                  filterable
                  :options="teleopNodeOptions"
                  placeholder="选择待命点或充电点"
                />
                <NButton size="small" type="primary" @click="handleTeleopCommand('move_to')">
                  发车前往
                </NButton>
              </div>
            </div>

            <!-- Emergency Stop & Reset -->
            <div class="pt-2 border-t border-slate-200 dark:border-slate-800 flex gap-3">
              <NButton
                type="error"
                size="small"
                class="flex-1 font-semibold"
                :disabled="!hasAuth('Fms.Fleet.Estop')"
                :title="hasAuth('Fms.Fleet.Estop') ? '紧急制动' : '缺少权限 Fms.Fleet.Estop'"
                @click="handleTeleopCommand('estop')"
              >
                <template #icon><div class="i-carbon-stop-filled-alt" /></template>
                急停
              </NButton>

              <NButton size="small" secondary class="flex-1" @click="handleTeleopCommand('clear_estop')">
                <template #icon><div class="i-carbon-renew" /></template>
                急停复位自愈
              </NButton>
            </div>
          </div>
        </NCard>
      </div>
    </NCard>
  </div>
</template>
