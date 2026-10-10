<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { fmsApi } from '@/service/api/fms';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';
import { nodeCaption, orderStatusLabel, vehicleCaption } from '@/utils/fms-display';

const mapStore = useMapStore();
const simStore = useSimulationStore();

// RCS Industry Profiles State
const profiles = ref<Fms.RcsProfileDto[]>([]);
const activeProfile = ref<Fms.RcsProfileDto | null>(null);
const selectedProfileId = ref('standard');
const switchingProfile = ref(false);

// Connection state
const customIp = ref('127.0.0.1');
const customPort = ref(9999);
const timeoutMs = ref(3000);
const retryCount = ref(3);
const isOnline = ref(true);
const pingLatency = ref<number | null>(null);
const isPinging = ref(false);
const saving = ref(false);
const saveSuccessMsg = ref('');

// Endpoints configuration
interface CallbackEndpoint {
  key: string;
  name: string;
  desc: string;
  path: string;
  method: string;
  status: 'idle' | 'testing' | 'success' | 'failed';
  lastRtt?: number;
  lastTestedAt?: string;
  lastError?: string;
}

const endpoints = ref<CallbackEndpoint[]>([
  {
    key: 'task_info',
    name: '任务下发响应',
    desc: '上位机派发任务时，FMS接收确认及执行态返回',
    path: '/api/v1/xinsong/task_info',
    method: 'POST',
    status: 'idle'
  },
  {
    key: 'task_arrive_target',
    name: '到点状态上报',
    desc: '车辆物理姿态进入目标站点容差范围时的即时通知',
    path: '/api/v1/xinsong/task_arrive_target',
    method: 'POST',
    status: 'idle'
  },
  {
    key: 'robot_permiss_start_action',
    name: '动作前置许可',
    desc: '举升、翻转、滚筒对接前请求上位机安全机构联锁放行',
    path: '/api/v1/xinsong/robot_permiss_start_action',
    method: 'POST',
    status: 'idle'
  },
  {
    key: 'check_cargo_rfid',
    name: '条码/RFID核验',
    desc: '车载读头感应物料载具RFID后与WMS账目一致性握手',
    path: '/api/v1/xinsong/check_cargo_rfid',
    method: 'POST',
    status: 'idle'
  },
  {
    key: 'task_finish',
    name: '任务完工终结',
    desc: '物理动作完成、载具脱离并释放全部独占路径资源',
    path: '/api/v1/xinsong/task_finish',
    method: 'POST',
    status: 'idle'
  },
  {
    key: 'task_delete',
    name: '任务取消注销',
    desc: '上位机下发任务撤销或紧急熔断指令后的清理响应',
    path: '/api/v1/xinsong/task_delete',
    method: 'POST',
    status: 'idle'
  }
]);

// Prefix Routing Simulator
const testTaskType = ref('PA_01_FEED_MAT');
const routePrefixResult = computed(() => {
  const parts = testTaskType.value.split('_');
  if (parts.length > 1 && parts[0]) {
    return {
      hasPrefix: true,
      prefix: parts[0],
      routedPath: `/${parts[0]}`,
      fullUrl: `http://${customIp.value}:${customPort.value}/${parts[0]}${endpoints.value[0].path}`
    };
  }
  return {
    hasPrefix: false,
    prefix: 'DEFAULT',
    routedPath: '/',
    fullUrl: `http://${customIp.value}:${customPort.value}${endpoints.value[0].path}`
  };
});

// Push Config (kcfms.xml)
const pushConfig = reactive({
  server: '127.0.0.1',
  port: 9999,
  enable: true,
  url: '/api/v1/xinsong/report_robot_status',
  intervalMs: 250,
  totalPushed: 0,
  lastPushedTime: '暂无推送记录'
});

// Protocol Debug Sandbox
const sandbox = reactive({
  taskSerial: `EXT-${Date.now().toString().slice(-6)}`,
  taskType: 'task',
  sourcePoint: '',
  targetPoint: '',
  robotId: '',
  cargoId: '',
  priority: 60,
  sending: false,
  responseLog: '',
  trackSerial: ''
});

const sandboxNodeOptions = computed(() =>
  (mapStore.currentGraph?.nodes || [])
    .filter(node => node.type === 'StandbyPoint' || node.type === 'JobPoint' || node.type === 'PullOverPoint')
    .map(node => ({ label: nodeCaption(node.id, mapStore.currentGraph?.nodes), value: node.id }))
);

const sandboxVehicleOptions = computed(() => [
  { label: '自动匹配空闲车', value: '' },
  ...simStore.vehicles.map(vehicle => ({ label: vehicleCaption(vehicle.id, simStore.vehicles), value: vehicle.id }))
]);

const trackedOrder = computed(() => simStore.activeOrders.find(order => order.serial === sandbox.trackSerial) || null);
const trackedVehicle = computed(() => {
  if (!trackedOrder.value?.assignedVehicleId) return null;
  return simStore.vehicles.find(v => v.id === trackedOrder.value!.assignedVehicleId) || null;
});

function fillSandboxDefaults() {
  const points = (mapStore.currentGraph?.nodes || []).filter(node => node.type === 'StandbyPoint' || node.type === 'JobPoint');
  if (!sandbox.sourcePoint && points[0]) sandbox.sourcePoint = points[0].id;
  if (!sandbox.targetPoint && points[1]) sandbox.targetPoint = points[1].id;
}

async function pingGateway() {
  isPinging.value = true;
  const start = performance.now();
  try {
    const res = await fmsApi.getRcsHeartbeat();
    const duration = Math.round(performance.now() - start);
    pingLatency.value = duration;
    isOnline.value = res.result;
  } catch {
    pingLatency.value = null;
    isOnline.value = false;
  } finally {
    isPinging.value = false;
  }
}

async function testEndpoint(ep: CallbackEndpoint) {
  ep.status = 'testing';
  ep.lastError = undefined;
  try {
    const res = await fmsApi.testEndpoint({ key: ep.key, path: ep.path, timeoutMs: timeoutMs.value });
    ep.lastRtt = res.rttMs;
    ep.lastTestedAt = new Date().toLocaleTimeString();
    if (res.success) {
      ep.status = 'success';
    } else {
      ep.status = 'failed';
      ep.lastError = res.error || `HTTP ${res.status || 0}`;
    }
  } catch (e: any) {
    ep.status = 'failed';
    ep.lastRtt = undefined;
    ep.lastTestedAt = new Date().toLocaleTimeString();
    ep.lastError = e?.message || '探测失败';
  }
}

async function loadProfiles() {
  try {
    const res = await fmsApi.getRcsProfiles();
    if (res && res.profiles && res.profiles.length > 0) {
      profiles.value = res.profiles;
      activeProfile.value = res.activeProfile || res.profiles.find(p => p.isActive) || res.profiles[0];
      selectedProfileId.value = activeProfile.value?.id || 'standard';
      if (activeProfile.value?.defaultTaskTypePrefix) {
        testTaskType.value = `${activeProfile.value.defaultTaskTypePrefix}_01_FEED`;
      }
    }
  } catch (e) {
    console.error('Failed to load RCS profiles', e);
  }
}

async function handleProfileSelect(profileId: string) {
  switchingProfile.value = true;
  try {
    const res = await fmsApi.setActiveRcsProfile(profileId);
    if (res.success && res.data) {
      activeProfile.value = res.data;
      selectedProfileId.value = res.data.id;
      if (res.data.defaultTaskTypePrefix) {
        testTaskType.value = `${res.data.defaultTaskTypePrefix}_01_TRANSFER`;
        sandbox.taskType = `${res.data.defaultTaskTypePrefix}_TASK`;
      }
      saveSuccessMsg.value = `已成功激活行业配置预设: ${res.data.name} (${res.data.vendor})`;
      setTimeout(() => { saveSuccessMsg.value = ''; }, 4000);
    }
  } catch (err: any) {
    alert(`切换协议失败: ${err.message}`);
  } finally {
    switchingProfile.value = false;
  }
}

async function saveCurrentProfile() {
  if (!activeProfile.value) return;
  saving.value = true;
  try {
    const res = await fmsApi.saveRcsProfile(activeProfile.value);
    if (res.success) {
      saveSuccessMsg.value = `协议与物理动力学参数已成功持久化并热重载生效！`;
      setTimeout(() => { saveSuccessMsg.value = ''; }, 4000);
    }
  } catch (err: any) {
    alert(`保存 Profile 异常: ${err.message}`);
  } finally {
    saving.value = false;
  }
}

async function loadConfig() {
  try {
    const bundle = await fmsApi.getConfigBundle();
    if (bundle.customSet) {
      try {
        const parsed = JSON.parse(bundle.customSet);
        if (parsed.ip) customIp.value = parsed.ip;
        if (parsed.port) customPort.value = parsed.port;
        if (parsed.urls) {
          endpoints.value.forEach(ep => {
            if (parsed.urls[ep.key]) {
              ep.path = parsed.urls[ep.key];
            }
          });
        }
      } catch (e) {
        console.error('CustomSet parse error', e);
      }
    }

    if (bundle.kcfmsXml) {
      try {
        const parsedPush = JSON.parse(bundle.kcfmsXml);
        if (parsedPush.server) pushConfig.server = parsedPush.server;
        if (parsedPush.port) pushConfig.port = parsedPush.port;
        if (parsedPush.enable !== undefined) pushConfig.enable = parsedPush.enable;
        if (parsedPush.url) pushConfig.url = parsedPush.url;
      } catch (e) {
        console.error('KcFmsXml parse error', e);
      }
    }

    if (bundle.pushStats) {
      pushConfig.totalPushed = bundle.pushStats.totalPushed ?? 0;
      pushConfig.lastPushedTime = bundle.pushStats.lastPushedAt
        ? new Date(bundle.pushStats.lastPushedAt).toLocaleString()
        : '暂无推送记录';
    }
  } catch (err: any) {
    console.error('Load config failed', err);
  }
}

async function saveAllConfig() {
  saving.value = true;
  saveSuccessMsg.value = '';
  try {
    const urlMap: Record<string, string> = {};
    endpoints.value.forEach(ep => {
      urlMap[ep.key] = ep.path;
    });

    const customSetJson = JSON.stringify({
      ip: customIp.value,
      port: Number(customPort.value),
      urls: urlMap
    });

    const kcfmsXmlJson = JSON.stringify({
      server: pushConfig.server,
      port: Number(pushConfig.port),
      enable: pushConfig.enable,
      url: pushConfig.url
    });

    const res1 = await fmsApi.updateCustomSet(customSetJson, 'Admin');
    const res2 = await fmsApi.updateKcfmsXml(kcfmsXmlJson, 'Admin');

    if (activeProfile.value) {
      await fmsApi.saveRcsProfile(activeProfile.value);
    }

    if (res1.success && res2.success) {
      saveSuccessMsg.value = 'RCS 北向通信、协议预设与状态推送配置已成功持久化并热重载生效！';
      setTimeout(() => {
        saveSuccessMsg.value = '';
      }, 4000);
    } else {
      alert(`保存失败: ${res1.message || res2.message}`);
    }
  } catch (err: any) {
    alert(`保存异常: ${err.message}`);
  } finally {
    saving.value = false;
  }
}

// 1-Click Project Sandbox Presets
function applySandboxPreset(presetKey: 'txc' | 'nxp' | 'sandisk' | 'nxp-tj' | 'standard') {
  const points = (mapStore.currentGraph?.nodes || []).filter(node => node.type === 'StandbyPoint' || node.type === 'JobPoint');
  const src = points[0]?.id || '101';
  const dst = points[1]?.id || points[0]?.id || '102';

  if (presetKey === 'txc') {
    handleProfileSelect('txc');
    sandbox.taskSerial = `TXC-${Date.now().toString().slice(-4)}`;
    sandbox.taskType = 'PA_14_TRANSFER';
    sandbox.sourcePoint = src;
    sandbox.targetPoint = dst;
    sandbox.cargoId = 'FOUP-TXC-3001';
    sandbox.priority = 85;
  } else if (presetKey === 'nxp') {
    handleProfileSelect('nxp-tw');
    sandbox.taskSerial = `NXP-PB-${Date.now().toString().slice(-4)}`;
    sandbox.taskType = 'MHS_PASSBOX_TX';
    sandbox.sourcePoint = src;
    sandbox.targetPoint = dst;
    sandbox.cargoId = 'MAG-ERACK-08';
    sandbox.priority = 75;
  } else if (presetKey === 'sandisk') {
    handleProfileSelect('sandisk-mt');
    sandbox.taskSerial = `SD-ELEV-${Date.now().toString().slice(-4)}`;
    sandbox.taskType = 'ARS_CROSS_FLOOR';
    sandbox.sourcePoint = src;
    sandbox.targetPoint = dst;
    sandbox.cargoId = 'COVERTRAY-A02';
    sandbox.priority = 90;
  } else if (presetKey === 'nxp-tj') {
    handleProfileSelect('nxp-tj');
    sandbox.taskSerial = `TJ-MOLD-${Date.now().toString().slice(-4)}`;
    sandbox.taskType = 'MICA_MAGAZINE_FEED';
    sandbox.sourcePoint = src;
    sandbox.targetPoint = dst;
    sandbox.cargoId = 'MAGAZINE-TJ-55';
    sandbox.priority = 80;
  } else {
    handleProfileSelect('standard');
    sandbox.taskSerial = `SIA-STD-${Date.now().toString().slice(-4)}`;
    sandbox.taskType = 'task';
    sandbox.sourcePoint = src;
    sandbox.targetPoint = dst;
    sandbox.cargoId = 'CARRIER-STD-01';
    sandbox.priority = 50;
  }
}

async function sendSandboxTask() {
  if (!sandbox.targetPoint) {
    sandbox.responseLog = '// 请先选择终点点位';
    return;
  }
  sandbox.sending = true;
  sandbox.responseLog = '正在向调度系统发送 task_add 报文 ...';
  const start = performance.now();
  const body: Fms.RcsTaskAddRequest = {
    task_id: sandbox.taskSerial,
    task_type: sandbox.taskType || 'task',
    source_point: sandbox.sourcePoint ? Number(sandbox.sourcePoint) : undefined,
    target: Number(sandbox.targetPoint),
    robot_id: sandbox.robotId || undefined,
    goal_action: sandbox.sourcePoint ? undefined : 1,
    priority: sandbox.priority,
    shelf_code: sandbox.cargoId || undefined
  };
  try {
    const res = await fmsApi.addRcsTask(body);
    const cost = Math.round(performance.now() - start);
    sandbox.responseLog = JSON.stringify(
      {
        request: body,
        response: { Result: res.result, ErrMsg: (res as any).ErrMsg ?? res.errMsg ?? '' },
        roundTripMs: cost,
        activeProfile: activeProfile.value?.name ?? 'standard',
        simulationStatus: activeProfile.value?.autoPermitInSimulation ? '沙箱放行模式: 上位机离线自动模拟通过' : '真实模式: 依赖外部上位机响应'
      },
      null,
      2
    );
    if (res.result) {
      sandbox.trackSerial = sandbox.taskSerial;
      sandbox.taskSerial = `EXT-${Date.now().toString().slice(-6)}`;
      simStore.refreshOrders().catch(() => undefined);
    }
  } catch (e: any) {
    sandbox.responseLog = JSON.stringify({ request: body, error: e.message || '网络通信故障' }, null, 2);
  } finally {
    sandbox.sending = false;
  }
}

onMounted(async () => {
  await loadProfiles();
  await loadConfig();
  pingGateway();
  simStore.init().catch(() => undefined);
  if (!mapStore.currentGraph) await mapStore.loadCurrentMap();
  fillSandboxDefaults();
});
</script>

<template>
  <div class="w-full space-y-5 p-4 pb-12">
    <!-- Header Banner -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-4">
      <div class="flex items-center gap-4">
        <div class="flex h-12 w-12 items-center justify-center rounded-xl bg-sky-500/10 text-sky-500 border border-sky-500/20 shadow-sm">
          <div class="i-carbon-network-4 text-2xl" />
        </div>
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-lg font-semibold text-slate-900 dark:text-white">TM/TC 调度通信与行业协议适配中心</h1>
            <span class="inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-11px font-medium font-mono" :class="isOnline ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30' : 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/30'">
              <span class="h-1.5 w-1.5 rounded-full" :class="isOnline ? 'bg-emerald-500 animate-pulse' : 'bg-rose-500'" />
              {{ isOnline ? '网关在线' : '网关离线' }}
            </span>
            <span class="rounded bg-sky-50 dark:bg-sky-950/60 border border-sky-200 dark:border-sky-800 px-2 py-0.5 text-10px font-mono text-sky-600 dark:text-sky-400">Siasun TM-V1 / Multi-RCS Compatible</span>
          </div>
          <p class="text-13px text-slate-500 dark:text-slate-400 mt-1">
            充当标准新松交通管控调度核心 (TM/TC)，无缝兼容台湾晶技 (TXC)、高雄恩智浦 (NXP-TW)、上海晟碟 (SanDisk)、天津恩智浦 (NXP-TJ) 等多厂区非标流程
          </p>
        </div>
      </div>

      <div class="flex items-center gap-3">
        <button
          class="flex items-center gap-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/60 px-4 py-2 text-13px font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-all"
          :disabled="isPinging"
          @click="pingGateway"
        >
          <div class="i-carbon-renew text-base" :class="{ 'animate-spin': isPinging }" />
          <span>{{ pingLatency !== null ? `${pingLatency} ms` : 'Ping 网关心跳' }}</span>
        </button>
        <button
          class="flex items-center gap-2 rounded-xl bg-sky-600 px-5 py-2 text-13px font-semibold text-white shadow-md shadow-sky-600/25 hover:bg-sky-500 active:scale-95 transition-all disabled:opacity-50"
          :disabled="saving"
          @click="saveAllConfig"
        >
          <div class="i-carbon-save text-base" />
          <span>{{ saving ? '正在同步持久化...' : '保存并热加载配置' }}</span>
        </button>
      </div>
    </div>

    <!-- Alert / Banner on Save -->
    <div v-if="saveSuccessMsg" class="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-13px font-medium text-emerald-800 dark:text-emerald-300 flex items-center gap-3 shadow-sm transition-all">
      <div class="i-carbon-checkmark-filled text-xl text-emerald-500" />
      <span>{{ saveSuccessMsg }}</span>
    </div>

    <!-- Section 1: Multi-RCS Industrial Presets & Adaptations -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
      <div class="flex flex-col md:flex-row md:items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800/80 mb-5 gap-3">
        <div class="flex items-center gap-2.5">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-500/10 text-indigo-500">
            <div class="i-carbon-flow-stream text-lg" />
          </div>
          <div>
            <h2 class="text-14px font-bold text-slate-900 dark:text-white">行业协议预设配置 (RCS Profiles)</h2>
            <p class="text-11px text-slate-500">一键切换各厂区 RCS 上位机的协议差异、前缀路由、安全互锁与仿真动作动力学</p>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <span class="text-12px text-slate-500">当前激活方案:</span>
          <span class="rounded-full bg-indigo-50 dark:bg-indigo-950/60 border border-indigo-200 dark:border-indigo-800 px-3 py-0.5 text-12px font-bold font-mono text-indigo-600 dark:text-indigo-400">
            {{ activeProfile?.name || '新松标准 TM-V1' }}
          </span>
        </div>
      </div>

      <!-- Preset Cards Grid -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3.5 mb-6">
        <div
          v-for="p in profiles"
          :key="p.id"
          class="rounded-xl p-3.5 border cursor-pointer transition-all relative overflow-hidden flex flex-col justify-between"
          :class="selectedProfileId === p.id
            ? 'border-indigo-500 bg-indigo-50/40 dark:bg-indigo-950/30 shadow-md ring-1 ring-indigo-500/30'
            : 'border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/40 hover:border-slate-300 dark:hover:border-slate-700'"
          @click="handleProfileSelect(p.id)"
        >
          <div v-if="selectedProfileId === p.id" class="absolute top-0 right-0 bg-indigo-600 text-white text-[9px] font-bold px-2 py-0.5 rounded-bl">
            ACTIVE
          </div>
          <div>
            <div class="flex items-center gap-1.5 mb-1.5">
              <span class="text-12px font-bold text-slate-900 dark:text-white truncate">{{ p.name }}</span>
            </div>
            <p class="text-10px text-slate-500 dark:text-slate-400 line-clamp-2 leading-relaxed mb-3">{{ p.description }}</p>
          </div>
          <div class="pt-2 border-t border-slate-200/60 dark:border-slate-800 flex items-center justify-between text-[10px] text-slate-400 font-mono">
            <span>{{ p.vendor }}</span>
            <span :class="p.useTypePrefixOnCallback ? 'text-indigo-500 font-semibold' : 'text-slate-400'">
              {{ p.useTypePrefixOnCallback ? '/{type} 前缀' : '根路径' }}
            </span>
          </div>
        </div>
      </div>

      <!-- Active Profile Detailed Controls -->
      <div v-if="activeProfile" class="rounded-xl border border-indigo-200/70 dark:border-indigo-900/50 bg-indigo-500/[0.02] dark:bg-indigo-950/10 p-5 space-y-5">
        <div class="flex items-center justify-between border-b border-indigo-100 dark:border-indigo-900/40 pb-3">
          <div class="flex items-center gap-2">
            <span class="font-bold text-13px text-indigo-900 dark:text-indigo-200">{{ activeProfile.name }} 详细运行参数</span>
            <span class="text-11px text-slate-500 font-mono">({{ activeProfile.id }})</span>
          </div>
          <button
            class="text-11px font-semibold text-indigo-600 dark:text-indigo-400 hover:underline flex items-center gap-1"
            @click="saveCurrentProfile"
          >
            <div class="i-carbon-save text-xs" />
            <span>保存此 Profile 配置</span>
          </button>
        </div>

        <!-- Capability Toggles -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <!-- Switch 1: Auto Permit Simulation -->
          <div class="rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-3.5 flex items-start justify-between gap-3">
            <div>
              <div class="flex items-center gap-1.5">
                <span class="text-12px font-bold text-slate-800 dark:text-slate-200">离线仿真沙箱放行</span>
                <span class="rounded bg-emerald-100 dark:bg-emerald-950 text-emerald-700 dark:text-emerald-300 text-[10px] font-mono px-1.5 py-0.2">SIM</span>
              </div>
              <p class="text-11px text-slate-500 mt-1 leading-relaxed">
                若上位机或硬件联锁离线，系统自动模拟 0,0 动作放行，确保无硬件时全流程仿真不断流。
              </p>
            </div>
            <NSwitch v-model:value="activeProfile.autoPermitInSimulation" size="small" />
          </div>

          <!-- Switch 2: Auto RFID Check Simulation -->
          <div class="rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-3.5 flex items-start justify-between gap-3">
            <div>
              <div class="flex items-center gap-1.5">
                <span class="text-12px font-bold text-slate-800 dark:text-slate-200">物料 RFID 自动通过</span>
                <span class="rounded bg-blue-100 dark:bg-blue-950 text-blue-700 dark:text-blue-300 text-[10px] font-mono px-1.5 py-0.2">RFID</span>
              </div>
              <p class="text-11px text-slate-500 mt-1 leading-relaxed">
                放货前核验 RFID 载具号。仿真沙箱模式下默认自动校验一致，驱动机构平滑下降。
              </p>
            </div>
            <NSwitch v-model:value="activeProfile.autoRfidVerifiedInSimulation" size="small" />
          </div>

          <!-- Switch 3: Use Type Prefix -->
          <div class="rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-3.5 flex items-start justify-between gap-3">
            <div>
              <div class="flex items-center gap-1.5">
                <span class="text-12px font-bold text-slate-800 dark:text-slate-200">动态前缀回调路由</span>
                <span class="rounded bg-purple-100 dark:bg-purple-950 text-purple-700 dark:text-purple-300 text-[10px] font-mono px-1.5 py-0.2">/{type}</span>
              </div>
              <p class="text-11px text-slate-500 mt-1 leading-relaxed">
                回调路径自动注入任务类型首段前缀（如台湾晶技 <code class="font-mono">/PA/api/v1/xinsong/*</code>）。
              </p>
            </div>
            <NSwitch v-model:value="activeProfile.useTypePrefixOnCallback" size="small" />
          </div>
        </div>

        <!-- Kinematics & Timing Parameters Tuning -->
        <div>
          <h3 class="text-12px font-bold text-slate-700 dark:text-slate-300 mb-3 flex items-center gap-1.5">
            <div class="i-carbon-meter text-sm text-indigo-500" />
            <span>仿真运行动力学与工艺动作时序参数 (Kinematics & Dwell Timing)</span>
          </h3>
          <div class="grid grid-cols-2 md:grid-cols-6 gap-3">
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">巡航速度 (m/s)</label>
              <NInputNumber v-model:value="activeProfile.cruiseSpeed" size="small" :step="0.1" :min="0.2" :max="3.0" />
            </div>
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">转弯速度 (rad/s)</label>
              <NInputNumber v-model:value="activeProfile.turningSpeed" size="small" :step="0.1" :min="0.2" :max="2.5" />
            </div>
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">进站蠕行 (m/s)</label>
              <NInputNumber v-model:value="activeProfile.creepSpeed" size="small" :step="0.05" :min="0.05" :max="0.5" />
            </div>
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">举升/夹抱耗时 (s)</label>
              <NInputNumber v-model:value="activeProfile.liftDurationSec" size="small" :step="0.5" :min="1.0" :max="15.0" />
            </div>
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">传递窗等待 (s)</label>
              <NInputNumber v-model:value="activeProfile.passBoxDwellSec" size="small" :step="1.0" :min="0" :max="60.0" />
            </div>
            <div class="rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-2.5">
              <label class="block text-[11px] text-slate-500 mb-1">提升机乘梯 (s)</label>
              <NInputNumber v-model:value="activeProfile.elevatorDwellSec" size="small" :step="1.0" :min="0" :max="120.0" />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Section 2: Visual TM/TC 6-Stage Action Lifecycle Timeline -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
      <div class="flex items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800/80 mb-5">
        <div class="flex items-center gap-2.5">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-500">
            <div class="i-carbon-roadmap text-lg" />
          </div>
          <div>
            <h2 class="text-14px font-bold text-slate-900 dark:text-white">TM/TC 任务全生命周期流程仿真管线</h2>
            <p class="text-11px text-slate-500">新松 TM/TC 标准六阶工步状态机与上位机双向握手流</p>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-2 md:grid-cols-6 gap-3">
        <!-- Step 1 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 01</span>
            <span class="h-2 w-2 rounded-full bg-emerald-500" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">1. 下发任务</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">task_add</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            接收并校验任务号、取放货站点、目标载具及优先级，加入执行队列。
          </p>
        </div>

        <!-- Step 2 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 02</span>
            <span class="h-2 w-2 rounded-full bg-emerald-500" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">2. 状态回报</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">task_info</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            向上位机确认接收并上报分配车辆、开始执行时刻与第一步航向。
          </p>
        </div>

        <!-- Step 3 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 03</span>
            <span class="h-2 w-2 rounded-full bg-emerald-500" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">3. 到点通知</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">task_arrive_target</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            车辆进入目标点容差范围，通知上位机与机台开始准备物料交接。
          </p>
        </div>

        <!-- Step 4 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5" :class="activeProfile?.autoPermitInSimulation ? 'border-emerald-500/40 bg-emerald-500/5' : ''">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 04</span>
            <span class="h-2 w-2 rounded-full" :class="activeProfile?.autoPermitInSimulation ? 'bg-emerald-500 animate-pulse' : 'bg-slate-400'" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">4. 动作许可</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">permiss_start_action</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            举升前向上位机或光电对射请求安全放行。<span v-if="activeProfile?.autoPermitInSimulation" class="text-emerald-600 font-semibold">[沙箱自动放行已开]</span>
          </p>
        </div>

        <!-- Step 5 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5" :class="activeProfile?.autoRfidVerifiedInSimulation ? 'border-blue-500/40 bg-blue-500/5' : ''">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 05</span>
            <span class="h-2 w-2 rounded-full" :class="activeProfile?.autoRfidVerifiedInSimulation ? 'bg-blue-500 animate-pulse' : 'bg-slate-400'" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">5. RFID 核验</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">check_cargo_rfid</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            车载读头感应载具 RFID 与 MES/WMS 对比。<span v-if="activeProfile?.autoRfidVerifiedInSimulation" class="text-blue-600 font-semibold">[仿真校验自动一致]</span>
          </p>
        </div>

        <!-- Step 6 -->
        <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/60 p-3.5">
          <div class="flex items-center justify-between mb-2">
            <span class="text-11px font-mono font-bold text-indigo-500">阶段 06</span>
            <span class="h-2 w-2 rounded-full bg-emerald-500" />
          </div>
          <span class="block text-12px font-bold text-slate-800 dark:text-slate-200">6. 完工释放</span>
          <span class="block text-[10px] font-mono text-slate-400 mt-0.5">task_finish</span>
          <p class="text-[11px] text-slate-500 mt-2 leading-tight">
            机构复位、释放互锁与时空窗锁闭，车辆转入待机或就近充电。
          </p>
        </div>
      </div>
    </div>

    <!-- Section 3: Gateway Connection & Status Push Config -->
    <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
      <!-- Card A: custom.set Northbound Connection -->
      <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
        <div class="flex items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800/80 mb-5">
          <div class="flex items-center gap-2.5">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-500/10 text-sky-500">
              <div class="i-carbon-connection-signal text-lg" />
            </div>
            <div>
              <h2 class="text-14px font-bold text-slate-900 dark:text-white">上位机通信网关 (custom.set)</h2>
              <p class="text-11px text-slate-500">接收上位机指令并转发回调请求的主服务地址</p>
            </div>
          </div>
          <span class="rounded bg-sky-50 dark:bg-sky-950/60 border border-sky-200 dark:border-sky-800 px-2 py-0.5 text-11px font-mono text-sky-600 dark:text-sky-400">
            HTTP REST / JSON
          </span>
        </div>

        <div class="space-y-4">
          <div class="grid grid-cols-3 gap-3">
            <div class="col-span-2">
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">主机</label>
              <input
                v-model="customIp"
                type="text"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20 transition-all"
                placeholder="127.0.0.1"
              />
            </div>
            <div>
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">端口</label>
              <input
                v-model.number="customPort"
                type="number"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20 transition-all"
                placeholder="9999"
              />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">超时（毫秒）</label>
              <input
                v-model.number="timeoutMs"
                type="number"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20 transition-all"
                placeholder="3000"
              />
            </div>
            <div>
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">网络失败重试 (Retry Limit)</label>
              <input
                v-model.number="retryCount"
                type="number"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20 transition-all"
                placeholder="3"
              />
            </div>
          </div>

          <div class="rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200 dark:border-slate-800 p-3 text-12px flex items-center justify-between">
            <span class="text-slate-500 font-mono">基准前向接入 URL:</span>
            <span class="font-mono font-semibold text-sky-600 dark:text-sky-400">http://{{ customIp }}:{{ customPort }}</span>
          </div>
        </div>
      </div>

      <!-- Card B: kcfms.xml Status Push Stream -->
      <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
        <div class="flex items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800/80 mb-5">
          <div class="flex items-center gap-2.5">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-500">
              <div class="i-carbon-chart-multitype text-lg" />
            </div>
            <div>
              <h2 class="text-14px font-bold text-slate-900 dark:text-white">高频遥测状态推送 (kcfms.xml)</h2>
              <p class="text-11px text-slate-500">FMS以高频帧向上位机主动上报车体姿态、电量与故障码</p>
            </div>
          </div>
          <label class="relative inline-flex items-center cursor-pointer">
            <input v-model="pushConfig.enable" type="checkbox" class="sr-only peer" />
            <div class="w-11 h-6 bg-slate-200 peer-focus:outline-none rounded-full peer dark:bg-slate-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-600"></div>
          </label>
        </div>

        <div class="space-y-4">
          <div class="grid grid-cols-3 gap-3">
            <div class="col-span-2">
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">推送接收端 IP</label>
              <input
                v-model="pushConfig.server"
                type="text"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20 transition-all"
              />
            </div>
            <div>
              <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">推送端口</label>
              <input
                v-model.number="pushConfig.port"
                type="number"
                class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20 transition-all"
              />
            </div>
          </div>

          <div>
            <label class="block text-11px font-medium text-slate-600 dark:text-slate-400 mb-1">推送目标路径 (Endpoint Path)</label>
            <input
              v-model="pushConfig.url"
              type="text"
              class="w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900 px-3.5 py-2 text-13px font-mono text-slate-900 dark:text-white focus:outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20 transition-all"
            />
          </div>

          <div class="grid grid-cols-3 gap-3 pt-1">
            <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/60 p-3">
              <span class="block text-10px font-mono text-slate-500">推送频率周期</span>
              <span class="text-13px font-bold font-mono text-emerald-600 dark:text-emerald-400">100 ms (10 Hz)</span>
            </div>
            <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/60 p-3">
              <span class="block text-10px font-mono text-slate-500">累计推送</span>
              <span class="text-13px font-bold font-mono text-slate-800 dark:text-slate-200">{{ pushConfig.totalPushed }}</span>
              <span class="block text-10px text-slate-400 mt-0.5">{{ pushConfig.lastPushedTime }}</span>
            </div>
            <div class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/60 p-3">
              <span class="block text-10px font-mono text-slate-500">推送状态</span>
              <span class="text-13px font-bold font-mono" :class="pushConfig.enable ? 'text-emerald-500' : 'text-slate-400'">
                {{ pushConfig.enable ? '● 正在推送' : '○ 已挂起' }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Section 4: Underscore Prefix Routing Rule & Interactive Simulator -->
    <div class="rounded-2xl border border-blue-500/30 bg-blue-500/5 dark:bg-blue-950/20 p-6 shadow-sm">
      <div class="flex items-center gap-3 mb-3">
        <div class="flex h-9 w-9 items-center justify-center rounded-lg bg-blue-500/20 text-blue-600 dark:text-blue-400">
          <div class="i-carbon-direction-fork text-xl" />
        </div>
        <div>
          <h2 class="text-sm font-semibold text-slate-900 dark:text-slate-100">任务类型与动态路径前缀路由</h2>
          <p class="text-12px text-blue-800 dark:text-blue-300">
            当启用前缀路由时，任务类型以首个下划线分割（如 <code class="font-mono font-bold">PA_14_get</code>），回调将自动定向至 <code class="font-mono font-bold">/PA/api/v1/xinsong/*</code>
          </p>
        </div>
      </div>

      <!-- Live Simulator Bar -->
      <div class="mt-4 rounded-xl border border-blue-200 dark:border-blue-900/60 bg-white dark:bg-slate-900 p-4">
        <div class="flex flex-col md:flex-row items-center gap-4">
          <div class="w-full md:w-1/3">
            <label class="block text-11px font-mono text-slate-500 dark:text-slate-400 mb-1">测试任务类型 (Task Type)</label>
            <input
              v-model="testTaskType"
              type="text"
              class="w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 px-3 py-1.5 text-13px font-mono font-semibold text-blue-600 dark:text-blue-400 focus:outline-none focus:border-blue-500"
              placeholder="例如: PA_14_get"
            />
          </div>

          <div class="flex-1 w-full grid grid-cols-1 md:grid-cols-3 gap-3">
            <div class="rounded-lg bg-slate-50 dark:bg-slate-800/80 p-2.5">
              <span class="block text-10px font-mono text-slate-500">解析前缀 Prefix</span>
              <span class="text-13px font-mono font-bold text-emerald-600 dark:text-emerald-400">
                {{ routePrefixResult.hasPrefix ? `/${routePrefixResult.prefix}` : '无前缀 (根路径)' }}
              </span>
            </div>

            <div class="col-span-2 rounded-lg bg-slate-50 dark:bg-slate-800/80 p-2.5">
              <span class="block text-10px font-mono text-slate-500">动态计算回调目标基准 URL</span>
              <span class="text-12px font-mono font-medium text-slate-800 dark:text-slate-200 truncate block" :title="routePrefixResult.fullUrl">
                {{ routePrefixResult.fullUrl }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Section 5: Northbound Callback Endpoint Registry Table -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] overflow-hidden shadow-sm">
      <div class="p-6 border-b border-slate-100 dark:border-slate-800/80 flex items-center justify-between">
        <div>
          <h2 class="text-sm font-semibold text-slate-900 dark:text-white">标准北向回调接口清单</h2>
          <p class="text-11px text-slate-500 mt-0.5">标准 RCS 协议要求的 6 项核心回调事件路径与连通性探测</p>
        </div>
        <span class="rounded-full bg-slate-100 dark:bg-slate-800 px-3 py-1 text-11px font-mono text-slate-600 dark:text-slate-400">
          共 6 项核心回调服务
        </span>
      </div>

      <div class="overflow-x-auto">
        <table class="w-full text-left text-xs border-collapse table-fixed">
          <colgroup>
            <col style="width: 220px;" />
            <col style="width: 200px;" />
            <col style="width: 90px;" />
            <col style="width: 260px;" />
            <col style="width: 120px;" />
            <col style="width: 130px;" />
          </colgroup>
          <thead class="border-b border-slate-100 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/40 text-11px font-semibold uppercase text-slate-500">
            <tr>
              <th class="px-4 py-3">事件</th>
              <th class="px-4 py-3">业务定义</th>
              <th class="px-4 py-3">协议方法</th>
              <th class="px-4 py-3">路径</th>
              <th class="px-4 py-3">链路状态</th>
              <th class="px-4 py-3 text-right">操作与探测</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-slate-800 font-normal text-slate-700 dark:text-slate-300">
            <tr v-for="ep in endpoints" :key="ep.key" class="hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors">
              <td class="px-4 py-3 truncate">
                <span class="font-mono font-bold text-sky-600 dark:text-sky-400">{{ ep.key }}</span>
                <span class="block text-[11px] text-slate-400 truncate">{{ ep.name }}</span>
              </td>
              <td class="px-4 py-3 text-xs text-slate-500 truncate" :title="ep.desc">
                {{ ep.desc }}
              </td>
              <td class="px-4 py-3 font-mono">
                <NTag size="tiny" type="info" :bordered="false">{{ ep.method }}</NTag>
              </td>
              <td class="px-4 py-3">
                <NInput
                  v-model:value="ep.path"
                  size="small"
                  class="font-mono text-xs"
                />
              </td>
              <td class="px-4 py-3">
                <span v-if="ep.status === 'idle'" class="inline-flex items-center gap-1.5 text-xs text-slate-400 font-mono">
                  <span class="h-1.5 w-1.5 rounded-full bg-slate-400" />
                  待测试
                </span>
                <span v-else-if="ep.status === 'testing'" class="inline-flex items-center gap-1.5 text-xs text-sky-500 font-mono">
                  <div class="i-carbon-renew text-xs animate-spin" />
                  握手中...
                </span>
                <span v-else-if="ep.status === 'success'" class="inline-flex items-center gap-1.5 text-xs text-emerald-600 dark:text-emerald-400 font-mono font-semibold">
                  <span class="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                  {{ ep.lastRtt != null ? `HTTP 成功 (${ep.lastRtt}ms)` : '探测成功' }}
                </span>
                <span v-else-if="ep.status === 'failed'" class="inline-flex items-center gap-1.5 text-xs text-rose-600 dark:text-rose-400 font-mono font-semibold" :title="ep.lastError">
                  <span class="h-1.5 w-1.5 rounded-full bg-rose-500" />
                  {{ ep.lastError || '探测失败' }}
                </span>
              </td>
              <td class="px-4 py-3 text-right">
                <NButton
                  size="tiny"
                  secondary
                  type="primary"
                  :loading="ep.status === 'testing'"
                  @click="testEndpoint(ep)"
                >
                  测试连通性
                </NButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Section 6: Protocol Debug & Multi-Project Sandbox -->
    <div class="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#11151f] p-6 shadow-sm">
      <div class="flex flex-col md:flex-row md:items-center justify-between pb-4 border-b border-slate-100 dark:border-slate-800/80 mb-5 gap-3">
        <div class="flex items-center gap-2.5">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-500/10 text-indigo-500">
            <div class="i-carbon-terminal text-lg" />
          </div>
          <div>
            <h2 class="text-sm font-semibold text-slate-900 dark:text-white">全流程报文联调沙箱 (Task Simulation Sandbox)</h2>
            <p class="text-11px text-slate-500">支持一键装载不同厂区真实项目报文，驱动车辆在地图上真实移动并执行工艺动作</p>
          </div>
        </div>

        <!-- 1-Click Project Sample Presets -->
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-11px text-slate-400 font-mono">快速载入项目用例:</span>
          <button
            class="px-2.5 py-1 rounded-lg border border-indigo-200 dark:border-indigo-800 bg-indigo-50 dark:bg-indigo-950/60 text-indigo-600 dark:text-indigo-400 text-11px font-medium hover:bg-indigo-100 transition-all"
            @click="applySandboxPreset('txc')"
          >
            台湾晶技 (TXC)
          </button>
          <button
            class="px-2.5 py-1 rounded-lg border border-purple-200 dark:border-purple-800 bg-purple-50 dark:bg-purple-950/60 text-purple-600 dark:text-purple-400 text-11px font-medium hover:bg-purple-100 transition-all"
            @click="applySandboxPreset('nxp')"
          >
            高雄恩智浦 (NXP)
          </button>
          <button
            class="px-2.5 py-1 rounded-lg border border-amber-200 dark:border-amber-800 bg-amber-50 dark:bg-amber-950/60 text-amber-600 dark:text-amber-400 text-11px font-medium hover:bg-amber-100 transition-all"
            @click="applySandboxPreset('sandisk')"
          >
            上海晟碟 (SanDisk)
          </button>
          <button
            class="px-2.5 py-1 rounded-lg border border-teal-200 dark:border-teal-800 bg-teal-50 dark:bg-teal-950/60 text-teal-600 dark:text-teal-400 text-11px font-medium hover:bg-teal-100 transition-all"
            @click="applySandboxPreset('nxp-tj')"
          >
            天津恩智浦 (Mica)
          </button>
          <button
            class="px-2.5 py-1 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-slate-600 dark:text-slate-300 text-11px font-medium hover:bg-slate-100 transition-all"
            @click="applySandboxPreset('standard')"
          >
            标准新松
          </button>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <!-- Input Form -->
        <div class="space-y-4">
          <div class="grid grid-cols-3 gap-3">
            <div class="col-span-2">
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">任务编号 (task_id / task_serial)</label>
              <NInput v-model:value="sandbox.taskSerial" size="small" />
            </div>
            <div>
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">优先级 (priority)</label>
              <NInputNumber v-model:value="sandbox.priority" size="small" :min="1" :max="100" class="w-full" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">任务类型 (task_type)</label>
              <NInput v-model:value="sandbox.taskType" size="small" placeholder="task" />
            </div>
            <div>
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">载具/料盒编码 (cargo_id / shelf_code)</label>
              <NInput v-model:value="sandbox.cargoId" size="small" placeholder="例如 FOUP-01" />
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">起点取货 (source_point，可空)</label>
              <NSelect v-model:value="sandbox.sourcePoint" :options="sandboxNodeOptions" size="small" filterable clearable placeholder="留空则只前往终点" />
            </div>
            <div>
              <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">终点放货 (target)</label>
              <NSelect v-model:value="sandbox.targetPoint" :options="sandboxNodeOptions" size="small" filterable placeholder="选择点位" />
            </div>
          </div>

          <div>
            <label class="mb-1 block text-11px font-medium text-slate-600 dark:text-slate-400">指定车辆 (robot_id，留空由调度器最优派发)</label>
            <NSelect v-model:value="sandbox.robotId" :options="sandboxVehicleOptions" size="small" filterable />
          </div>

          <button
            class="w-full flex items-center justify-center gap-2 rounded-xl bg-indigo-600 py-2.5 text-13px font-semibold text-white shadow-md shadow-indigo-600/25 hover:bg-indigo-500 active:scale-98 transition-all disabled:opacity-50"
            :disabled="sandbox.sending"
            @click="sendSandboxTask"
          >
            <div class="i-carbon-send-alt text-base" :class="{ 'animate-pulse': sandbox.sending }" />
            <span>{{ sandbox.sending ? '正在派发仿真...' : '下发仿真任务 (POST task_add)' }}</span>
          </button>

          <!-- Realtime Tracked Order & Action Status -->
          <div v-if="trackedOrder" class="rounded-xl border border-indigo-200 dark:border-indigo-900 bg-indigo-50/30 dark:bg-indigo-950/20 p-3 text-12px space-y-2">
            <div class="flex items-center justify-between">
              <span class="font-mono font-bold text-indigo-700 dark:text-indigo-300">{{ trackedOrder.serial }}</span>
              <span class="rounded bg-indigo-100 dark:bg-indigo-900 px-2 py-0.5 text-10px font-medium text-indigo-700 dark:text-indigo-300">
                {{ orderStatusLabel(trackedOrder.status) }}
              </span>
            </div>
            <div class="flex items-center justify-between text-slate-600 dark:text-slate-300 text-11px">
              <span>{{ vehicleCaption(trackedOrder.assignedVehicleId, simStore.vehicles) }}</span>
              <span>第 {{ trackedOrder.currentStep ?? 0 }} / {{ trackedOrder.totalSteps ?? 0 }} 步</span>
            </div>
            <div v-if="trackedOrder.stepName" class="text-11px font-mono text-slate-500">
              当前工步: <span class="text-slate-800 dark:text-slate-200 font-semibold">{{ trackedOrder.stepName }}</span>
            </div>
            <div v-if="trackedVehicle" class="flex items-center gap-3 pt-1 text-[11px] font-mono text-slate-500 border-t border-indigo-100 dark:border-indigo-900/40">
              <span>动作阶段: <strong class="text-indigo-600 dark:text-indigo-400">{{ (trackedVehicle as any).actionPhase || 'Idle' }}</strong></span>
              <span>举升高度: <strong class="text-emerald-600">{{ Math.round(((trackedVehicle as any).liftProgress || 0) * 100) }}%</strong></span>
              <span>载货: <strong class="text-slate-700 dark:text-slate-200">{{ trackedVehicle.holdingPayload ? ((trackedVehicle as any).payloadCarrierId || '已载货') : '空载' }}</strong></span>
            </div>
            <div class="h-1.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
              <div class="h-full rounded-full bg-indigo-500 transition-[width] duration-300" :style="{ width: `${Math.max(4, Math.round((trackedOrder.progress ?? 0) * 100))}%` }" />
            </div>
          </div>
        </div>

        <!-- Terminal Output -->
        <div>
          <div class="flex items-center justify-between mb-1">
            <label class="block text-11px font-mono text-slate-500 dark:text-slate-400">应答内容与握手追踪 (Response Stream)</label>
            <span class="text-10px font-mono text-slate-400">JSON Output</span>
          </div>
          <pre
            class="h-64 w-full overflow-auto rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-900 dark:bg-black/80 p-4 font-mono text-11px text-emerald-400 leading-relaxed shadow-inner"
          >{{ sandbox.responseLog || '// 点击上方【快速载入项目用例】或选择点位后点击【下发仿真任务】\n// 请求与应答报文、仿真沙箱放行日志会实时打印在这里\n// 下发后车辆将在 2D/3D 地图上实时移动并执行工艺动作' }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>
