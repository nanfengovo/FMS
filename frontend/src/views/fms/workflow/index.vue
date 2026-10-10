<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuth } from '@/hooks/business/auth';
import { useMapStore } from '@/store/modules/map';
import { fmsApi } from '@/service/api/fms';

const router = useRouter();
const { checkPermission, currentRoleInfo } = useAuth();
const mapStore = useMapStore();

export type StepCategory = 'nav' | 'action' | 'verify' | 'mutex' | 'control';
export type StepStatus = 'ready' | 'waiting' | 'blocked' | 'fault';

export interface BranchCondition {
  type: 'bms_low' | 'rfid_mismatch' | 'cargo_weight' | 'io_signal';
  label: string;
  failTargetNode: string;
  failAction: string;
}

export interface WorkflowStep {
  id: string;
  category: StepCategory;
  name: string;
  actionType: string;
  nodeId: string;
  dwellSec: number;
  maxSpeed?: number;
  cargoId?: string;
  liftStrokeMm?: number;
  mutexZoneId?: string;
  timeoutSec?: number;
  retryCount?: number;
  status: StepStatus;
  statusDetail?: string;
  desc: string;
  branchCondition?: BranchCondition;
  conditionType?: string;
  conditionValue?: string;
  trueTargetStepId?: string;
  falseTargetStepId?: string;
  nextStepId?: string;
  isTerminal?: boolean;
  ioSignalName?: string;
  expectedIoValue?: string;
}

export interface ActionBrick {
  actionType: string;
  category: StepCategory;
  name: string;
  defaultDwell: number;
  icon: string;
  desc: string;
  defaultNode?: string;
}

export interface WorkflowTemplateItem {
  id: string;
  name: string;
  desc: string;
  isBuiltIn?: boolean;
  version?: number;
  versionTag?: string;
  changeLog?: string;
  createdAt?: string;
  updatedAt?: string;
  steps: WorkflowStep[];
}

// RDS Action Brick Palette Definitions
const actionBricks: Record<StepCategory, { title: string; icon: string; items: ActionBrick[] }> = {
  nav: {
    title: '导航与移动',
    icon: 'i-carbon-pedestrian',
    items: [
      { actionType: 'MOVE_TO_POINT', category: 'nav', name: '前往站点', defaultDwell: 1, icon: 'i-carbon-map-boundary', desc: '规划到目标站点' },
      { actionType: 'MOVE_ALONG_PATH', category: 'nav', name: '沿车道行驶', defaultDwell: 1, icon: 'i-carbon-chart-line-smooth', desc: '按指定车道走' },
      { actionType: 'PULL_OVER', category: 'nav', name: '避让停靠', defaultDwell: 2, icon: 'i-carbon-pause-outline', desc: '让出主干道' }
    ]
  },
  action: {
    title: '机构与工艺',
    icon: 'i-carbon-box',
    items: [
      { actionType: 'LIFT_UP', category: 'action', name: '顶升', defaultDwell: 3.5, icon: 'i-carbon-arrow-up', desc: '升起并锁定载具' },
      { actionType: 'PUT_DOWN', category: 'action', name: '下降', defaultDwell: 3.5, icon: 'i-carbon-arrow-down', desc: '放下载具' },
      { actionType: 'FORK_PICK', category: 'action', name: '叉取', defaultDwell: 4.5, icon: 'i-carbon-fork', desc: '从货位取出' },
      { actionType: 'FORK_DROP', category: 'action', name: '卸料', defaultDwell: 4.5, icon: 'i-carbon-download', desc: '放入目标货位' },
      { actionType: 'ROLLER_TRANSFER', category: 'action', name: '辊道对接', defaultDwell: 5.0, icon: 'i-carbon-rotate-clockwise', desc: '和机台辊道交接' }
    ]
  },
  verify: {
    title: '感知与校验',
    icon: 'i-carbon-scan',
    items: [
      { actionType: 'RFID_READ_VERIFY', category: 'verify', name: '载具校验', defaultDwell: 2, icon: 'i-carbon-id-management', desc: '核对载具标签' },
      { actionType: 'BARCODE_SCAN', category: 'verify', name: '扫码', defaultDwell: 1.5, icon: 'i-carbon-barcode', desc: '核对条码' },
      { actionType: 'WEIGHT_CHECK', category: 'verify', name: '称重', defaultDwell: 2, icon: 'i-carbon-scale', desc: '检查空载或超载' }
    ]
  },
  mutex: {
    title: '交管与互斥',
    icon: 'i-carbon-traffic-event',
    items: [
      { actionType: 'CLAIM_MUTEX_ZONE', category: 'mutex', name: '申请互斥区', defaultDwell: 1, icon: 'i-carbon-locked', desc: '进入前先占锁' },
      { actionType: 'RELEASE_MUTEX_ZONE', category: 'mutex', name: '释放互斥区', defaultDwell: 1, icon: 'i-carbon-unlocked', desc: '驶离后解锁' },
      { actionType: 'TRAFFIC_PERMIT', category: 'mutex', name: '等待放行', defaultDwell: 2, icon: 'i-carbon-stop-sign', desc: '等道闸或信号灯' }
    ]
  },
  control: {
    title: '控制与决策',
    icon: 'i-carbon-flow-stream',
    items: [
      { actionType: 'WAIT_TIME', category: 'control', name: '等待', defaultDwell: 5, icon: 'i-carbon-timer', desc: '在站点停留一段时间' },
      { actionType: 'WAIT_IO_SIGNAL', category: 'control', name: '等机台信号', defaultDwell: 3, icon: 'i-carbon-connection-receive', desc: '信号到了再走' },
      { actionType: 'AUTO_CHARGE', category: 'control', name: '去充电', defaultDwell: 15, icon: 'i-carbon-charging-station', desc: '低电量时去充电桩' },
      { actionType: 'DECISION_BRANCH', category: 'control', name: '条件分支', defaultDwell: 1, icon: 'i-carbon-split', desc: '按电量或信号分流' }
    ]
  }
};

function fallbackStationId(prefer: 'standby' | 'charge' | 'any' = 'any') {
  const list = mapStore.currentGraph?.nodes || [];
  if (prefer === 'charge') {
    return list.find(n => n.type === 'ChargePoint')?.id || fallbackStationId('standby');
  }
  if (prefer === 'standby') {
    return list.find(n => n.type === 'StandbyPoint')?.id
      || list.find(n => n.type === 'PullOverPoint')?.id
      || list[0]?.id
      || '';
  }
  return list.find(n => n.type === 'StandbyPoint')?.id
    || list.find(n => n.type === 'ChargePoint')?.id
    || list.find(n => n.type === 'JobPoint')?.id
    || list[0]?.id
    || '';
}

function fallbackZoneId() {
  return mapStore.currentGraph?.mutexZones?.[0]?.id || '';
}

function mapStationError(): string | null {
  const currentNodes = new Set((mapStore.currentGraph?.nodes || []).map(n => n.id));
  const currentZones = new Set((mapStore.currentGraph?.mutexZones || []).map(z => z.id));
  const reasons: string[] = [];
  const missingNodes: string[] = [];
  const missingZones: string[] = [];
  steps.value.forEach(s => {
    if (s.nodeId && currentNodes.size > 0 && !currentNodes.has(s.nodeId) && !missingNodes.includes(s.nodeId)) {
      missingNodes.push(s.nodeId);
      reasons.push(`工步「${s.name}」的站点 ${s.nodeId} 不在路网「${mapStore.currentGraph?.name || '当前路网'}」上`);
    }
    if (s.mutexZoneId && currentZones.size > 0 && !currentZones.has(s.mutexZoneId) && !missingZones.includes(s.mutexZoneId)) {
      missingZones.push(s.mutexZoneId);
      reasons.push(`工步「${s.name}」的互斥区 ${s.mutexZoneId} 不在当前路网上`);
    }
  });
  if (reasons.length === 0) return null;
  return '禁止下发：' + reasons.join('；');
}

function brickName(actionType?: string) {
  if (!actionType) return '';
  for (const group of Object.values(actionBricks)) {
    const hit = group.items.find(item => item.actionType === actionType);
    if (hit) return hit.name;
  }
  return actionType;
}

const brickOptions = Object.values(actionBricks).flatMap(group =>
  group.items.map(item => ({ label: item.name, value: item.actionType }))
);

function stageCaption(name?: string) {
  if (!name) return '';
  return name.replace(/^Stage\s+/i, '阶段 ');
}

// 状态定义
const templates = ref<WorkflowTemplateItem[]>([]);
const selectedTemplateId = ref('foup-flow');
const steps = ref<WorkflowStep[]>([]);
const activeStepIndex = ref(0);
const activeStep = computed(() => steps.value[activeStepIndex.value] || steps.value[0]);
const lastSavedTime = ref('');
const isSaving = ref(false);

const currentTemplate = computed(() => templates.value.find(t => t.id === selectedTemplateId.value));
const isCurrentBuiltIn = computed(() => !!currentTemplate.value?.isBuiltIn);

// 视图模式: pipeline (顺序流水线列表) | diagram (拓扑连线图) | block (块运行时矩阵)
const viewMode = ref<'pipeline' | 'diagram' | 'block'>('diagram');
const isDiagramExpanded = ref(false);
const diagramScale = ref(1.0);

function zoomIn() {
  diagramScale.value = Math.min(1.8, Math.round((diagramScale.value + 0.15) * 100) / 100);
}
function zoomOut() {
  diagramScale.value = Math.max(0.6, Math.round((diagramScale.value - 0.15) * 100) / 100);
}
function resetZoom() {
  diagramScale.value = 1.0;
}

function handleToggleBranch(enable: boolean) {
  if (!activeStep.value) return;
  if (enable) {
    if (activeStep.value.actionType === 'DECISION_BRANCH') {
      activeStep.value.conditionType = activeStep.value.conditionType || 'SOC_LESS_THAN';
      activeStep.value.conditionValue = activeStep.value.conditionValue || '25.0';
      activeStep.value.trueTargetStepId = activeStep.value.trueTargetStepId || 'STEP-10';
      activeStep.value.falseTargetStepId = activeStep.value.falseTargetStepId || 'STEP-11';
    }
    activeStep.value.branchCondition = {
      type: 'bms_low',
      label: '低电量自动回充',
      failTargetNode: fallbackStationId('charge'),
      failAction: 'AUTO_CHARGE'
    };
  } else {
    delete activeStep.value.branchCondition;
    if (activeStep.value.actionType === 'DECISION_BRANCH') {
      activeStep.value.trueTargetStepId = undefined;
      activeStep.value.falseTargetStepId = undefined;
    }
  }
}

const stepTargetOptions = computed(() => {
  const opts = steps.value.map(s => ({
    label: `${s.id}: ${s.name} (${s.actionType})`,
    value: s.id
  }));
  opts.push({ label: 'END (直接终结工单)', value: 'END' });
  return opts;
});

// 产线外部 IO 信号状态与虚拟机台控制器 (Virtual PLC Console)
const ioSignals = ref<Record<string, string>>({
  DOCK_READY: '0',
  LINE_CLEAR: '0',
  MATERIAL_ARRIVED: '0'
});
const isIoPanelVisible = ref(true);

async function fetchIoSignals() {
  try {
    const res = await fmsApi.getIoSignals();
    if (res.success && res.signals) {
      ioSignals.value = res.signals;
    }
  } catch (e) {
    console.error('Failed to fetch IO signals:', e);
  }
}

async function handleToggleSignal(signalName: string) {
  try {
    const res = await fmsApi.toggleIoSignal(signalName);
    if (res.success) {
      ioSignals.value[signalName] = res.value;
      window.$message?.success(`信号 [${signalName}] 已切换为: ${res.value}`);
    }
  } catch (err: any) {
    window.$message?.error(`切换信号异常: ${err.message}`);
  }
}

async function handleSetSignal(signalName: string, val: string) {
  try {
    const res = await fmsApi.setIoSignal(signalName, val);
    if (res.success) {
      ioSignals.value[signalName] = res.value;
      window.$message?.success(`信号 [${signalName}] 已设为: ${res.value}`);
    }
  } catch (err: any) {
    window.$message?.error(`设定信号异常: ${err.message}`);
  }
}

export interface DiagramStepItem {
  step: WorkflowStep;
  idx: number;
  x: number;
  y: number;
  hasBranch: boolean;
  branchX?: number;
  branchY?: number;
}

export interface BlockStageItem {
  stageId: string;
  stageName: string;
  stageSubName: string;
  category: StepCategory;
  badgeColor: string;
  bgColor: string;
  steps: { step: WorkflowStep; originalIdx: number }[];
  totalDwell: number;
  startY: number;
  height: number;
}

const diagramData = computed(() => {
  const startX = 240;
  const startY = 30;
  const stepGap = 135;
  const firstStepY = 120;

  const stepItems: DiagramStepItem[] = steps.value.map((s, idx) => {
    const y = firstStepY + idx * stepGap;
    const hasBranch = !!s.branchCondition || s.actionType === 'DECISION_BRANCH' || s.actionType === 'RFID_READ_VERIFY' || s.actionType === 'AUTO_CHARGE';
    return {
      step: s,
      idx,
      x: startX,
      y,
      hasBranch,
      branchX: hasBranch ? startX + 260 : undefined,
      branchY: hasBranch ? y : undefined
    };
  });

  const endY = firstStepY + steps.value.length * stepGap + 20;
  const hasAnyBranch = stepItems.some(i => i.hasBranch);
  const totalWidth = hasAnyBranch ? 720 : 480;
  const totalHeight = endY + 90;

  return {
    startX,
    startY,
    endY,
    stepItems,
    totalWidth,
    totalHeight
  };
});

const blockStages = computed<BlockStageItem[]>(() => {
  const result: BlockStageItem[] = [];
  if (steps.value.length === 0) return result;

  let currentCategory = steps.value[0].category;
  let currentGroup: { step: WorkflowStep; originalIdx: number }[] = [];

  const stageMeta: Record<StepCategory, { name: string; sub: string; color: string; bg: string }> = {
    nav: { name: '空间寻址与车道循迹块', sub: 'Navigation & Tracking Stage', color: '#3b82f6', bg: 'rgba(59, 130, 246, 0.05)' },
    action: { name: '机构工装与物料工艺块', sub: 'Mechanism & Tooling Stage', color: '#f59e0b', bg: 'rgba(245, 158, 11, 0.05)' },
    verify: { name: '感知鉴权与防错校验块', sub: 'Perception & Verification Stage', color: '#10b981', bg: 'rgba(16, 185, 129, 0.05)' },
    mutex: { name: '交管防冲与互斥时空窗块', sub: 'Traffic Mutex & Safety Stage', color: '#ef4444', bg: 'rgba(239, 68, 68, 0.05)' },
    control: { name: '状态机分支与时序决策块', sub: 'Control & Smart Branching Stage', color: '#8b5cf6', bg: 'rgba(139, 92, 246, 0.05)' }
  };

  steps.value.forEach((step, idx) => {
    if (step.category !== currentCategory && currentGroup.length > 0) {
      const meta = stageMeta[currentCategory] || { name: '通用执行块', sub: 'Execution Block', color: '#64748b', bg: 'rgba(100, 116, 139, 0.05)' };
      const startIdx = currentGroup[0].originalIdx;
      const endIdx = currentGroup[currentGroup.length - 1].originalIdx;
      const stepGap = 135;
      const firstStepY = 120;
      const startY = firstStepY + startIdx * stepGap - 20;
      const height = (endIdx - startIdx) * stepGap + 120;

      result.push({
        stageId: `stage-${result.length + 1}`,
        stageName: `阶段 ${result.length + 1}：${meta.name}`,
        stageSubName: meta.sub,
        category: currentCategory,
        badgeColor: meta.color,
        bgColor: meta.bg,
        steps: [...currentGroup],
        totalDwell: currentGroup.reduce((sum, item) => sum + (item.step.dwellSec || 0), 0),
        startY,
        height
      });
      currentCategory = step.category;
      currentGroup = [];
    }
    currentGroup.push({ step, originalIdx: idx });
  });

  if (currentGroup.length > 0) {
    const meta = stageMeta[currentCategory] || { name: '通用执行块', sub: 'Execution Block', color: '#64748b', bg: 'rgba(100, 116, 139, 0.05)' };
    const startIdx = currentGroup[0].originalIdx;
    const endIdx = currentGroup[currentGroup.length - 1].originalIdx;
    const stepGap = 135;
    const firstStepY = 120;
    const startY = firstStepY + startIdx * stepGap - 20;
    const height = (endIdx - startIdx) * stepGap + 120;

    result.push({
      stageId: `stage-${result.length + 1}`,
      stageName: `阶段 ${result.length + 1}：${meta.name}`,
      stageSubName: meta.sub,
      category: currentCategory,
      badgeColor: meta.color,
      bgColor: meta.bg,
      steps: [...currentGroup],
      totalDwell: currentGroup.reduce((sum, item) => sum + (item.step.dwellSec || 0), 0),
      startY,
      height
    });
  }

  return result;
});

// 模态框状态
const showPreflightModal = ref(false);
const preflightReport = ref<any>(null);

const showCreateModal = ref(false);
const createForm = reactive({
  name: '',
  id: '',
  desc: '',
  baseOn: 'blank' // 'blank' | 'clone'
});

const showRunModal = ref(false);
const runForm = reactive({
  taskSerial: '',
  vehicleId: '',
  priority: 80
});
const availableVehicles = ref<any[]>([]);

// 版本历史与差异对比状态
const showVersionDiffModal = ref(false);
const historyList = ref<any[]>([]);
const selectedHistoryId = ref('');
const isLoadingHistory = ref(false);

const selectedHistory = computed(() => {
  return historyList.value.find(h => h.id === selectedHistoryId.value) || historyList.value[0];
});

async function openVersionDiffModal() {
  isLoadingHistory.value = true;
  showVersionDiffModal.value = true;
  try {
    const list = await fmsApi.getWorkflowHistory(selectedTemplateId.value);
    historyList.value = list;
    if (list.length > 0) {
      selectedHistoryId.value = list[0].id;
    }
  } catch (err: any) {
    console.error('Failed to load version history:', err);
    window.$message?.error('拉取版本历史失败');
  } finally {
    isLoadingHistory.value = false;
  }
}

// 步骤差异项定义与比较引擎
export interface StepDiffItem {
  type: 'added' | 'removed' | 'modified' | 'unchanged';
  stepId: string;
  stepName: string;
  currentStep?: WorkflowStep;
  baseStep?: any;
  diffDetails?: string[];
}

const diffResults = computed<{
  items: StepDiffItem[];
  addedCount: number;
  removedCount: number;
  modifiedCount: number;
  unchangedCount: number;
}>(() => {
  const baseSteps: any[] = selectedHistory.value?.steps || [];
  const currSteps = steps.value;
  const items: StepDiffItem[] = [];

  let addedCount = 0;
  let removedCount = 0;
  let modifiedCount = 0;
  let unchangedCount = 0;

  const maxLen = Math.max(baseSteps.length, currSteps.length);
  for (let i = 0; i < maxLen; i++) {
    const b = baseSteps[i];
    const c = currSteps[i];

    if (!b && c) {
      items.push({
        type: 'added',
        stepId: c.id,
        stepName: c.name,
        currentStep: c,
        diffDetails: [`[+新增工步] 动作: ${c.actionType}, 站点: ${c.nodeId}, 耗时: ${c.dwellSec}s`]
      });
      addedCount++;
    } else if (b && !c) {
      items.push({
        type: 'removed',
        stepId: b.id,
        stepName: b.name,
        baseStep: b,
        diffDetails: [`[-移除原工步] 动作: ${b.actionType}, 原站点: ${b.nodeId}`]
      });
      removedCount++;
    } else if (b && c) {
      const details: string[] = [];
      if (b.actionType !== c.actionType) details.push(`动作代码: ${b.actionType} → ${c.actionType}`);
      if (b.nodeId !== c.nodeId) details.push(`目标站点: ${b.nodeId} → ${c.nodeId}`);
      if (b.dwellSec !== c.dwellSec) details.push(`停留耗时: ${b.dwellSec}s → ${c.dwellSec}s`);
      if ((b.mutexZoneId || '') !== (c.mutexZoneId || '')) details.push(`互斥防冲区: ${b.mutexZoneId || '无'} → ${c.mutexZoneId || '无'}`);
      if ((b.cargoId || '') !== (c.cargoId || '')) details.push(`载具物料: ${b.cargoId || '无'} → ${c.cargoId || '无'}`);
      if (b.name !== c.name) details.push(`工步名称: ${b.name} → ${c.name}`);

      if (details.length > 0) {
        items.push({
          type: 'modified',
          stepId: c.id,
          stepName: c.name,
          currentStep: c,
          baseStep: b,
          diffDetails: details
        });
        modifiedCount++;
      } else {
        items.push({
          type: 'unchanged',
          stepId: c.id,
          stepName: c.name,
          currentStep: c,
          baseStep: b
        });
        unchangedCount++;
      }
    }
  }

  return { items, addedCount, removedCount, modifiedCount, unchangedCount };
});

function handleRollbackToSelectedVersion() {
  if (!selectedHistory.value) return;
  steps.value = JSON.parse(JSON.stringify(selectedHistory.value.steps));
  activeStepIndex.value = 0;
  showVersionDiffModal.value = false;
  window.$message?.success(`已成功恢复步骤至历史版本【${selectedHistory.value.versionTag || ('v' + selectedHistory.value.version)}】！请核验后保存`);
}

// 保存模板弹窗表单
const showSaveModal = ref(false);
const saveForm = reactive({
  versionTag: '',
  changeLog: ''
});

function openSaveModal() {
  if (!checkPermission('Fms.Workflow.Edit', '保存流程模板')) return;
  if (steps.value.length === 0) {
    window.$message?.warning('流水线工步不能为空');
    return;
  }
  const nextVer = (currentTemplate.value?.version || 1) + 1;
  saveForm.versionTag = `v1.${nextVer}.0`;
  saveForm.changeLog = `优化工步流水线与互斥配置 (v1.${nextVer}.0)`;
  showSaveModal.value = true;
}

// 实时执行监控
const showExecutionMonitor = ref(false);
const currentRunningOrderId = ref('');
const executionData = ref<any>(null);
let executionPollTimer: any = null;

onMounted(async () => {
  if (!mapStore.currentGraph) {
    await mapStore.loadCurrentMap();
  }
  await loadTemplates();
  await fetchIoSignals();
});

onUnmounted(() => {
  stopExecutionPolling();
});

// 加载全量模板
async function loadTemplates() {
  try {
    const list = await fmsApi.getWorkflowTemplates();
    if (list && list.length > 0) {
      templates.value = list.map((item: any) => ({
        id: item.id,
        name: item.name,
        desc: item.desc,
        isBuiltIn: item.isBuiltIn,
        version: item.version || 1,
        versionTag: item.versionTag || `v1.${item.version || 1}.0`,
        changeLog: item.changeLog || '初始创建',
        createdAt: item.createdAt,
        updatedAt: item.updatedAt,
        steps: (item.steps || []).map((s: any) => {
          let branch = s.branchCondition;
          if (!branch && s.actionType === 'RFID_READ_VERIFY') {
            branch = {
              type: 'rfid_mismatch',
              label: 'RFID比对不符',
              failTargetNode: fallbackStationId('standby'),
              failAction: 'PULL_OVER'
            };
          } else if (!branch && s.actionType === 'DECISION_BRANCH') {
            branch = {
              type: 'bms_low',
              label: 'SOC < 25% 回充',
              failTargetNode: fallbackStationId('charge'),
              failAction: 'AUTO_CHARGE'
            };
          }
          return {
            ...s,
            status: s.status || 'ready',
            branchCondition: branch,
            conditionType: s.conditionType || (s.actionType === 'DECISION_BRANCH' ? 'SOC_LESS_THAN' : undefined),
            conditionValue: s.conditionValue || (s.actionType === 'DECISION_BRANCH' ? '25.0' : undefined),
            trueTargetStepId: s.trueTargetStepId || (s.actionType === 'DECISION_BRANCH' ? 'STEP-10' : undefined),
            falseTargetStepId: s.falseTargetStepId || (s.actionType === 'DECISION_BRANCH' ? 'STEP-11' : undefined),
            nextStepId: s.nextStepId,
            isTerminal: s.isTerminal || false,
            ioSignalName: s.ioSignalName,
            expectedIoValue: s.expectedIoValue || '1'
          };
        })
      }));

      // 如果当前选中的模板在列表中，切换到它；否则选第一个
      const matched = templates.value.find(t => t.id === selectedTemplateId.value);
      if (matched) {
        steps.value = JSON.parse(JSON.stringify(matched.steps));
      } else if (templates.value.length > 0) {
        selectedTemplateId.value = templates.value[0].id;
        steps.value = JSON.parse(JSON.stringify(templates.value[0].steps));
      }
    }
  } catch (err: any) {
    console.error('Failed to load templates from server:', err);
    window.$message?.warning('从服务器拉取模板失败，将使用本地模式');
  }
}

// 切换模板
function switchTemplate(tplId: string) {
  selectedTemplateId.value = tplId;
  const target = templates.value.find(t => t.id === tplId);
  if (target) {
    steps.value = JSON.parse(JSON.stringify(target.steps));
    activeStepIndex.value = 0;
    window.$message?.info(`已载入工序：${target.name}`);
  }
}

// 创建新模板
function openCreateModal() {
  createForm.name = `新建工序_${new Date().toLocaleTimeString('zh-CN', { hour12: false })}`;
  createForm.id = `wf-${Date.now().toString().slice(-6)}`;
  createForm.desc = '用于自动化搬运与协同作业的自定义流水线';
  createForm.baseOn = 'clone';
  showCreateModal.value = true;
}

function handleConfirmCreate() {
  if (!createForm.name.trim()) {
    window.$message?.warning('请输入工序名称');
    return;
  }

  const newId = createForm.id.trim() || `wf-user-${Date.now().toString().slice(-6)}`;
  let initialSteps: WorkflowStep[] = [];

  if (createForm.baseOn === 'clone' && steps.value.length > 0) {
    initialSteps = JSON.parse(JSON.stringify(steps.value));
  } else {
    // 空白模版默认单步
    const defaultNode = fallbackStationId();
    initialSteps = [
      {
        id: 'STEP-01',
        category: 'nav',
        name: '驶向起始工位',
        actionType: 'MOVE_TO_POINT',
        nodeId: defaultNode,
        dwellSec: 1,
        maxSpeed: 1.5,
        status: 'ready',
        desc: '移动至工位准备执行工艺'
      }
    ];
  }

  const newTpl: WorkflowTemplateItem = {
    id: newId,
    name: createForm.name.trim(),
    desc: createForm.desc.trim(),
    isBuiltIn: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    steps: initialSteps
  };

  templates.value.push(newTpl);
  selectedTemplateId.value = newId;
  steps.value = initialSteps;
  activeStepIndex.value = 0;
  showCreateModal.value = false;

  window.$message?.success(`已创建新工序：【${newTpl.name}】，请配置并保存`);
  handleSaveTemplate();
}

// 确认保存弹窗提交
async function handleConfirmSave() {
  await handleSaveTemplate();
}

// 保存当前模板到后端
async function handleSaveTemplate() {
  if (!checkPermission('Fms.Workflow.Edit', '保存流程模板')) return;

  if (steps.value.length === 0) {
    window.$message?.warning('流水线工步不能为空');
    return;
  }

  isSaving.value = true;
  try {
    const curVer = currentTemplate.value?.version || 1;
    const tpl: WorkflowTemplateItem = {
      id: selectedTemplateId.value,
      name: currentTemplate.value?.name || '未命名流程',
      desc: currentTemplate.value?.desc || '',
      isBuiltIn: isCurrentBuiltIn.value,
      version: curVer + 1,
      versionTag: saveForm.versionTag || `v1.${curVer + 1}.0`,
      changeLog: saveForm.changeLog || '工步优化与互斥配置变更',
      steps: steps.value
    };

    const res = await fmsApi.saveWorkflowTemplate(tpl);
    if (res.success) {
      lastSavedTime.value = new Date().toLocaleTimeString();
      const finalTag = res.data?.versionTag || tpl.versionTag;
      window.$message?.success(`流程【${tpl.name}】(${finalTag}) 已成功持久化至后端模板库！`);
      showSaveModal.value = false;

      // 同步本地缓存备份
      try {
        localStorage.setItem(`fms_workflow_backup_${tpl.id}`, JSON.stringify(tpl));
      } catch {}

      // 更新列表内该项
      const idx = templates.value.findIndex(t => t.id === tpl.id);
      if (idx >= 0) {
        templates.value[idx].steps = JSON.parse(JSON.stringify(steps.value));
        templates.value[idx].version = res.data?.version || tpl.version;
        templates.value[idx].versionTag = res.data?.versionTag || tpl.versionTag;
        templates.value[idx].changeLog = res.data?.changeLog || tpl.changeLog;
        templates.value[idx].updatedAt = new Date().toISOString();
      }
    } else {
      window.$message?.error(`保存失败: ${res.message}`);
    }
  } catch (err: any) {
    window.$message?.error(`保存异常: ${err.message}`);
  } finally {
    isSaving.value = false;
  }
}

// 删除自定义模板
async function handleDeleteTemplate(tplId: string) {
  const target = templates.value.find(t => t.id === tplId);
  if (!target) return;
  if (target.isBuiltIn) {
    window.$message?.warning('内置工业模板受保护，不可删除');
    return;
  }

  try {
    const res = await fmsApi.deleteWorkflowTemplate(tplId);
    if (res.success) {
      window.$message?.success(`工序【${target.name}】已删除`);
      templates.value = templates.value.filter(t => t.id !== tplId);
      if (selectedTemplateId.value === tplId) {
        selectedTemplateId.value = templates.value[0]?.id || 'foup-flow';
        switchTemplate(selectedTemplateId.value);
      }
    } else {
      window.$message?.error(`删除失败: ${res.message}`);
    }
  } catch (err: any) {
    window.$message?.error(`删除异常: ${err.message}`);
  }
}

// Category Tag & Color Helpers
function getCategoryInfo(category: StepCategory) {
  switch (category) {
    case 'nav':
      return { label: '导航移动', tagType: 'info' as const, bg: 'bg-blue-500/10 text-blue-600 dark:text-blue-400 border-blue-500/30' };
    case 'action':
      return { label: '机构动作', tagType: 'warning' as const, bg: 'bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/30' };
    case 'verify':
      return { label: '识别校验', tagType: 'success' as const, bg: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30' };
    case 'mutex':
      return { label: '交管互斥', tagType: 'error' as const, bg: 'bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/30' };
    case 'control':
      return { label: '控制决策', tagType: 'default' as const, bg: 'bg-purple-500/10 text-purple-600 dark:text-purple-400 border-purple-500/30' };
    default:
      return { label: category, tagType: 'default' as const, bg: 'bg-slate-500/10 text-slate-600 border-slate-500/30' };
  }
}

function getStatusBadge(status: StepStatus) {
  switch (status) {
    case 'ready':
      return { text: '合规就绪', type: 'success' as const, dotClass: 'bg-emerald-500' };
    case 'waiting':
      return { text: '等待资源', type: 'warning' as const, dotClass: 'bg-amber-500 animate-pulse' };
    case 'blocked':
      return { text: '卡点避让', type: 'error' as const, dotClass: 'bg-red-500 animate-ping' };
    case 'fault':
      return { text: '超时熔断', type: 'error' as const, dotClass: 'bg-purple-500' };
    default:
      return { text: status, type: 'default' as const, dotClass: 'bg-slate-400' };
  }
}

// Add Brick to Pipeline
function handleAddBrick(brick: ActionBrick) {
  const newIndex = steps.value.length + 1;
  const newId = `STEP-${String(newIndex).padStart(2, '0')}`;
  const defaultNode = fallbackStationId();

  const newStep: WorkflowStep = {
    id: newId,
    category: brick.category,
    name: brick.name,
    actionType: brick.actionType,
    nodeId: brick.defaultNode || defaultNode,
    dwellSec: brick.defaultDwell,
    maxSpeed: brick.category === 'nav' ? 1.5 : undefined,
    liftStrokeMm: brick.category === 'action' ? 100 : undefined,
    mutexZoneId: brick.category === 'mutex' ? fallbackZoneId() : undefined,
    timeoutSec: brick.category === 'mutex' ? 30 : undefined,
    retryCount: 1,
    status: 'ready',
    statusDetail: '配置就绪',
    desc: brick.desc
  };

  steps.value.push(newStep);
  activeStepIndex.value = steps.value.length - 1;
  window.$message?.success(`已追加动作积木：【${brick.name}】`);
}

// Step operations
function moveStep(idx: number, direction: 'up' | 'down') {
  if (direction === 'up' && idx > 0) {
    const temp = steps.value[idx];
    steps.value[idx] = steps.value[idx - 1];
    steps.value[idx - 1] = temp;
    activeStepIndex.value = idx - 1;
  } else if (direction === 'down' && idx < steps.value.length - 1) {
    const temp = steps.value[idx];
    steps.value[idx] = steps.value[idx + 1];
    steps.value[idx + 1] = temp;
    activeStepIndex.value = idx + 1;
  }
  reindexSteps();
}

function duplicateStep(idx: number) {
  const item = steps.value[idx];
  const clone: WorkflowStep = JSON.parse(JSON.stringify(item));
  clone.name = `${clone.name} (副本)`;
  steps.value.splice(idx + 1, 0, clone);
  reindexSteps();
  activeStepIndex.value = idx + 1;
  window.$message?.info(`已复制工序节点`);
}

function removeStep(idx: number) {
  if (steps.value.length <= 1) {
    window.$message?.warning('至少需要保留 1 个工序节点');
    return;
  }
  steps.value.splice(idx, 1);
  reindexSteps();
  if (activeStepIndex.value >= steps.value.length) {
    activeStepIndex.value = steps.value.length - 1;
  }
  window.$message?.info('工序节点已移除');
}

function reindexSteps() {
  steps.value.forEach((s, i) => {
    s.id = `STEP-${String(i + 1).padStart(2, '0')}`;
  });
}

function clearAllSteps() {
  steps.value = [
    {
      id: 'STEP-01',
      category: 'nav',
      name: '工位移动',
      actionType: 'MOVE_TO_POINT',
      nodeId: fallbackStationId(),
      dwellSec: 1,
      status: 'ready',
      desc: '规划最优路径移动至目标站点'
    }
  ];
  activeStepIndex.value = 0;
  window.$message?.warning('流水线已重置清空');
}

// Preflight Check
function runPreflightCheck() {
  const currentNodes = new Set((mapStore.currentGraph?.nodes || []).map(n => n.id));
  const currentZones = new Set((mapStore.currentGraph?.mutexZones || []).map(z => z.id));

  const missingNodes: string[] = [];
  const missingZones: string[] = [];
  let totalDwell = 0;

  steps.value.forEach(s => {
    totalDwell += s.dwellSec || 0;
    if (s.nodeId && !currentNodes.has(s.nodeId) && currentNodes.size > 0) {
      if (!missingNodes.includes(s.nodeId)) missingNodes.push(s.nodeId);
    }
    if (s.mutexZoneId && !currentZones.has(s.mutexZoneId) && currentZones.size > 0) {
      if (!missingZones.includes(s.mutexZoneId)) missingZones.push(s.mutexZoneId);
    }
  });

  preflightReport.value = {
      valid: missingNodes.length === 0 && missingZones.length === 0,
    totalSteps: steps.value.length,
    totalDwell,
    missingNodes,
    missingZones,
    mapName: mapStore.currentGraph?.name || '未知地图',
    availableNodesCount: currentNodes.size
  };

  showPreflightModal.value = true;
}

// 打开运行配置对话框
async function openRunModal() {
  if (!checkPermission('Fms.Workflow.Run', '流程试运行')) return;

  runForm.taskSerial = `WF-${new Date().toLocaleTimeString('zh-CN', { hour12: false }).replace(/:/g, '')}-${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  runForm.priority = 80;
  runForm.vehicleId = '';

  // 获取系统在线可用车辆
  try {
    const simState = await fmsApi.getSimulationState();
    availableVehicles.value = (simState?.vehicles || []).map((v: any) => ({
      label: `${v.name || v.id} (${v.status}, 电量 ${Math.round(v.battery)}%)`,
      value: v.id,
      status: v.status,
      battery: v.battery
    }));
  } catch {
    availableVehicles.value = [];
  }

  showRunModal.value = true;
}

// 确认下发运行工序流水线
async function handleConfirmRun() {
  if (steps.value.length === 0) {
    window.$message?.warning('工序步骤不能为空');
    return;
  }
  const stationError = mapStationError();
  if (stationError) {
    window.$message?.error(stationError);
    return;
  }

  try {
    const res = await fmsApi.runWorkflow({
      templateId: selectedTemplateId.value,
      taskSerial: runForm.taskSerial,
      vehicleId: runForm.vehicleId || undefined,
      priority: runForm.priority,
      steps: steps.value
    });

    if (res.success && res.orderId) {
      showRunModal.value = false;
      window.$message?.success(`工序已下发，正在仿真里跟踪 ${res.orderSerial}`);
      currentRunningOrderId.value = res.orderId;
      showExecutionMonitor.value = true;
      startExecutionPolling(res.orderId);
      router.push({ path: '/fms/simulation', query: { trace: res.orderId } });
    } else {
      window.$message?.error(`下发失败: ${res.error}`);
    }
  } catch (err: any) {
    window.$message?.error(`下发执行异常: ${err.message}`);
  }
}

// 启动执行态轮询
function startExecutionPolling(orderId: string) {
  stopExecutionPolling();
  fetchExecutionStatus(orderId);
  fetchIoSignals();
  executionPollTimer = setInterval(() => {
    fetchExecutionStatus(orderId);
    fetchIoSignals();
  }, 800);
}

function stopExecutionPolling() {
  if (executionPollTimer) {
    clearInterval(executionPollTimer);
    executionPollTimer = null;
  }
}

async function fetchExecutionStatus(orderId: string) {
  try {
    const res = await fmsApi.getWorkflowExecution(orderId);
    if (res.success) {
      executionData.value = res;
      // 若任务已经终态，停止高频轮询
      if (['Done', 'Completed', 'Cancelled', 'Failed'].includes(res.status)) {
        stopExecutionPolling();
      }
    }
  } catch (err) {
    console.error('Failed to poll workflow execution:', err);
  }
}

// 监控台操作：跳步与取消
async function handleSkipStep() {
  if (!currentRunningOrderId.value) return;
  try {
    const res = await fmsApi.controlWorkflowExecution(currentRunningOrderId.value, 'skip');
    if (res.success) {
      window.$message?.success('已跳过当前工步，推进至下一步');
      fetchExecutionStatus(currentRunningOrderId.value);
    } else {
      window.$message?.error(res.message || '跳步失败');
    }
  } catch (err: any) {
    window.$message?.error(`操作异常: ${err.message}`);
  }
}

async function handleCancelExecution() {
  if (!currentRunningOrderId.value) return;
  try {
    const res = await fmsApi.controlWorkflowExecution(currentRunningOrderId.value, 'cancel', '用户在工作流监控台中中止');
    if (res.success) {
      window.$message?.warning('已向引擎下发任务中止指令');
      fetchExecutionStatus(currentRunningOrderId.value);
    } else {
      window.$message?.error(res.message || '中止失败');
    }
  } catch (err: any) {
    window.$message?.error(`操作异常: ${err.message}`);
  }
}

function handleGoSimulation() {
  router.push('/fms/simulation');
}

// Export JSON
function handleExportJson() {
  const jsonStr = JSON.stringify(
    {
      templateId: selectedTemplateId.value,
      name: currentTemplate.value?.name,
      desc: currentTemplate.value?.desc,
      steps: steps.value,
      exportedAt: new Date().toISOString()
    },
    null,
    2
  );

  const blob = new Blob([jsonStr], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `workflow_${selectedTemplateId.value}.json`;
  a.click();
  URL.revokeObjectURL(url);
  window.$message?.success('已导出工序编排 JSON 配置文件');
}
</script>

<template>
  <div class="flex h-[calc(100vh-108px)] min-h-0 flex-col gap-3 overflow-hidden p-3">
    <!-- Header Card -->
    <NCard size="medium" class="shadow-sm">
      <div class="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-base font-bold text-slate-800 dark:text-slate-100">
              流程编排
            </h1>
            <NTag size="tiny" type="info" round :bordered="false">{{ currentRoleInfo.label }}</NTag>
            <NTag v-if="lastSavedTime" size="tiny" type="success" round :bordered="false">
              已保存 {{ lastSavedTime }}
            </NTag>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
            把导航、机构和等待拼成一条工序，再放到仿真里跑。
          </p>
        </div>

        <NSpace align="center" :size="8">
          <NButton size="small" secondary @click="runPreflightCheck">
            <template #icon><div class="i-carbon-checkmark-outline" /></template>
            拓扑预检
          </NButton>
          <NButton size="small" secondary @click="handleExportJson">
            <template #icon><div class="i-carbon-document-export" /></template>
            导出 JSON
          </NButton>
          <NButton size="small" secondary @click="openVersionDiffModal">
            <template #icon><div class="i-carbon-version text-primary" /></template>
            版本历史 ({{ currentTemplate?.versionTag || 'v1.0.0' }})
          </NButton>
          <NButton size="small" secondary @click="openCreateModal">
            <template #icon><div class="i-carbon-add-alt text-primary" /></template>
            新建工序
          </NButton>
          <NButton size="small" :loading="isSaving" type="primary" secondary @click="openSaveModal">
            <template #icon><div class="i-carbon-save" /></template>
            保存工序
          </NButton>
          <NButton type="primary" size="small" @click="openRunModal">
            <template #icon><div class="i-carbon-play-filled-alt" /></template>
            一键运行
          </NButton>
        </NSpace>
      </div>

      <!-- Preset Template Selector Bar -->
      <div class="mt-4 p-3 bg-slate-50 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 flex flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-xs font-semibold text-slate-700 dark:text-slate-300">工序库选择:</span>
          <NButtonGroup size="small">
            <NButton
              v-for="tpl in templates"
              :key="tpl.id"
              :type="selectedTemplateId === tpl.id ? 'primary' : 'default'"
              secondary
              @click="switchTemplate(tpl.id)"
            >
              <template #icon>
                <div v-if="tpl.isBuiltIn" class="i-carbon-cube text-slate-400 text-xs" />
                <div v-else class="i-carbon-user text-primary text-xs" />
              </template>
              {{ tpl.name }}
            </NButton>
          </NButtonGroup>
        </div>

        <div class="flex items-center gap-2">
          <NPopconfirm
            v-if="!isCurrentBuiltIn"
            @positive-click="handleDeleteTemplate(selectedTemplateId)"
          >
            <template #trigger>
              <NButton size="tiny" quaternary type="error">
                <template #icon><div class="i-carbon-trash-can" /></template>
                删除当前流程
              </NButton>
            </template>
            确认删除自定义流程【{{ currentTemplate?.name }}】吗？此操作不可逆。
          </NPopconfirm>

          <NButton size="tiny" tertiary @click="clearAllSteps">
            <template #icon><div class="i-carbon-clean" /></template>
            清空步骤
          </NButton>
          <span class="text-[11px] text-gray-400 font-mono">共 {{ steps.length }} 个有序工步</span>
        </div>
      </div>
    </NCard>

    <!-- Virtual PLC & IO Signals Controller Console -->
    <NCard size="small" class="shadow-sm border border-emerald-500/20 bg-gradient-to-r from-emerald-500/5 via-sky-500/5 to-transparent">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex items-center gap-2">
          <div class="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
          <span class="font-bold text-xs text-slate-800 dark:text-slate-100 flex items-center gap-1.5">
            <span class="i-carbon-chip text-emerald-600 text-sm" />
            机台信号
          </span>
          <span class="text-[11px] text-slate-400 hidden sm:inline">
            拨动后，等待信号的工步会放行
          </span>
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <!-- Signal Pills with Quick Toggle -->
          <div
            v-for="(val, sigName) in ioSignals"
            :key="sigName"
            class="flex items-center gap-1.5 px-2.5 py-1 rounded-lg border text-xs transition"
            :class="[
              val === '1'
                ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-700 dark:text-emerald-300'
                : 'bg-slate-100 dark:bg-slate-800/80 border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-400'
            ]"
          >
            <span
              class="w-2 h-2 rounded-full transition-colors"
              :class="val === '1' ? 'bg-emerald-500 shadow-[0_0_6px_#10b981]' : 'bg-slate-400'"
            />
            <span class="font-mono font-bold">{{ sigName }}</span>
            <span class="font-mono font-bold px-1 rounded text-[10px]" :class="val === '1' ? 'bg-emerald-500 text-white' : 'bg-slate-300 dark:bg-slate-700 text-slate-700 dark:text-slate-300'">
              {{ val }}
            </span>
            <button
              class="ml-1 px-1.5 py-0.5 rounded text-[10px] font-medium hover:bg-emerald-600 hover:text-white transition cursor-pointer"
              :class="val === '1' ? 'bg-emerald-500/20 text-emerald-700 dark:text-emerald-300' : 'bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-300'"
              @click="handleToggleSignal(sigName)"
              :title="`翻转 ${sigName} 信号状态 (当前: ${val})`"
            >
              {{ val === '1' ? '置 0' : '置 1' }}
            </button>
          </div>

          <NButton size="tiny" quaternary @click="fetchIoSignals" title="刷新 IO 信号状态">
            <template #icon><div class="i-carbon-renew" /></template>
            刷新
          </NButton>
        </div>
      </div>
    </NCard>

    <!-- Main 3-Column Studio Layout: Left Action Bricks + Center Flow Sequence + Right Inspector -->
    <div class="grid min-h-0 flex-1 grid-cols-1 gap-3 overflow-hidden lg:grid-cols-12">
      <!-- Left Panel: Action Bricks Palette (3 cols) -->
      <div class="min-h-0 space-y-3 overflow-auto lg:col-span-3">
        <NCard title="动作积木" size="small" class="shadow-sm">
          <template #header-extra>
            <span class="text-[11px] text-slate-400">点击追加至工步</span>
          </template>

          <div class="max-h-[calc(100vh-340px)] space-y-4 overflow-y-auto pr-1">
            <div
              v-for="(group, catKey) in actionBricks"
              :key="catKey"
              class="space-y-1.5"
            >
              <div class="text-[11px] font-bold text-slate-600 dark:text-slate-300 flex items-center gap-1.5 border-b border-slate-100 dark:border-slate-800 pb-1">
                <div :class="group.icon" class="text-sm text-primary" />
                <span>{{ group.title }}</span>
              </div>

              <div class="space-y-1.5">
                <div
                  v-for="brick in group.items"
                  :key="brick.actionType"
                  class="p-2 rounded-lg border border-slate-200 dark:border-slate-800 hover:border-blue-400 dark:hover:border-blue-500 hover:shadow-sm bg-white dark:bg-slate-850 cursor-pointer transition flex items-center justify-between group"
                  @click="handleAddBrick(brick)"
                >
                  <div class="flex items-center gap-2 min-w-0 pr-2">
                    <div
                      class="w-6 h-6 rounded flex items-center justify-center text-xs shrink-0"
                      :class="getCategoryInfo(brick.category).bg"
                    >
                      <div :class="brick.icon" />
                    </div>
                    <div class="truncate">
                      <div class="text-xs font-medium text-slate-800 dark:text-slate-200 group-hover:text-blue-600 dark:group-hover:text-blue-400 truncate">
                        {{ brick.name }}
                      </div>
                      <div class="truncate text-[10px] text-slate-400">
                        {{ brick.desc }}
                      </div>
                    </div>
                  </div>

                  <div class="shrink-0 text-slate-300 group-hover:text-blue-500">
                    <div class="i-carbon-add-alt text-base" />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </NCard>
      </div>

      <!-- Center Panel: Workflow Pipeline / LogicFlow Canvas (5 or 9 cols) -->
      <div :class="[viewMode === 'diagram' && isDiagramExpanded ? 'lg:col-span-9' : 'lg:col-span-5', 'min-h-0 space-y-3 overflow-auto transition-all duration-300']">
        <NCard size="small" class="shadow-sm">
          <template #header>
            <div class="flex min-w-0 flex-wrap items-center gap-2">
              <span class="shrink-0 whitespace-nowrap text-xs font-bold">编排画布</span>
              <NButtonGroup size="tiny">
                <NButton
                  :type="viewMode === 'diagram' ? 'primary' : 'default'"
                  secondary
                  @click="viewMode = 'diagram'"
                >
                  <template #icon><div class="i-carbon-network-4 text-xs" /></template>
                  连线拓扑图
                </NButton>
                <NButton
                  :type="viewMode === 'block' ? 'primary' : 'default'"
                  secondary
                  @click="viewMode = 'block'"
                >
                  <template #icon><div class="i-carbon-grid text-xs" /></template>
                  块运行时矩阵
                </NButton>
                <NButton
                  :type="viewMode === 'pipeline' ? 'primary' : 'default'"
                  secondary
                  @click="viewMode = 'pipeline'"
                >
                  <template #icon><div class="i-carbon-list-bulleted text-xs" /></template>
                  流水线列表
                </NButton>
              </NButtonGroup>
            </div>
          </template>

          <template #header-extra>
            <NSpace :size="6" align="center">
              <template v-if="viewMode === 'diagram'">
                <NButton size="tiny" quaternary @click="zoomIn" title="放大">
                  <div class="i-carbon-zoom-in" />
                </NButton>
                <span class="text-[10px] font-mono text-slate-400 w-8 text-center">{{ Math.round(diagramScale * 100) }}%</span>
                <NButton size="tiny" quaternary @click="zoomOut" title="缩小">
                  <div class="i-carbon-zoom-out" />
                </NButton>
                <NButton size="tiny" quaternary @click="resetZoom" title="重置比例">
                  <div class="i-carbon-fit-to-screen" />
                </NButton>
                <div class="h-3 w-px bg-slate-200 dark:bg-slate-700" />
                <NButton
                  size="tiny"
                  quaternary
                  :type="isDiagramExpanded ? 'primary' : 'default'"
                  @click="isDiagramExpanded = !isDiagramExpanded"
                  :title="isDiagramExpanded ? '折叠宽屏' : '全宽展开'"
                >
                  <div :class="isDiagramExpanded ? 'i-carbon-minimize' : 'i-carbon-maximize'" />
                </NButton>
              </template>
              <span class="text-[11px] text-slate-400 font-mono">
                理论耗时: {{ steps.reduce((acc, s) => acc + (s.dwellSec || 0), 0) }}s
              </span>
            </NSpace>
          </template>

          <!-- Mode 1: LogicFlow Topology Diagram Canvas -->
          <div v-if="viewMode === 'diagram'" class="relative overflow-auto border border-slate-200 dark:border-slate-800 rounded-xl bg-slate-50/60 dark:bg-slate-900/50 min-h-[320px] max-h-[520px] select-none p-4">
            <div
              class="relative transition-transform duration-150 origin-top"
              :style="{
                transform: `scale(${diagramScale})`,
                width: `${diagramData.totalWidth}px`,
                minHeight: `${diagramData.totalHeight}px`
              }"
            >
              <!-- SVG Connector Lines Layer -->
              <svg
                class="absolute inset-0 pointer-events-none z-0"
                :width="diagramData.totalWidth"
                :height="diagramData.totalHeight"
              >
                <defs>
                  <pattern id="wf-grid" width="20" height="20" patternUnits="userSpaceOnUse">
                    <circle cx="2" cy="2" r="1" fill="#cbd5e1" class="dark:fill-slate-700" opacity="0.6" />
                  </pattern>
                  <marker id="wf-arrow" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                    <path d="M 0 1.5 L 8 5 L 0 8.5 z" fill="#3b82f6" />
                  </marker>
                  <marker id="wf-arrow-branch" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                    <path d="M 0 1.5 L 8 5 L 0 8.5 z" fill="#f59e0b" />
                  </marker>
                </defs>

                <rect width="100%" height="100%" fill="url(#wf-grid)" />

                <!-- Stage Block Containers (Nested Block Runtime Boundaries) -->
                <g v-for="stage in blockStages" :key="stage.stageId">
                  <rect
                    :x="35"
                    :y="stage.startY"
                    :width="diagramData.totalWidth - 55"
                    :height="stage.height"
                    rx="14"
                    :fill="stage.bgColor"
                    :stroke="stage.badgeColor"
                    stroke-width="1.2"
                    stroke-dasharray="5,3"
                    opacity="0.85"
                  />
                  <!-- Stage Header Pill inside SVG -->
                  <rect
                    :x="45"
                    :y="stage.startY - 10"
                    width="240"
                    height="20"
                    rx="4"
                    :fill="stage.badgeColor"
                  />
                  <text
                    :x="53"
                    :y="stage.startY + 4"
                    fill="#ffffff"
                    font-size="10"
                    font-weight="bold"
                  >
                    {{ stageCaption(stage.stageName) }}（{{ stage.steps.length }} 步 · {{ stage.totalDwell }} 秒）
                  </text>
                </g>

                <!-- Line: Start Node to Step 0 -->
                <path
                  v-if="diagramData.stepItems.length > 0"
                  :d="`M ${diagramData.startX} ${diagramData.startY + 28} L ${diagramData.startX} ${diagramData.stepItems[0].y}`"
                  stroke="#94a3b8"
                  stroke-width="2"
                  stroke-dasharray="4,3"
                  marker-end="url(#wf-arrow)"
                />

                <!-- Lines: Between Step i and Step i+1 -->
                <g v-for="(item, idx) in diagramData.stepItems" :key="'line-' + item.step.id">
                  <path
                    v-if="idx < diagramData.stepItems.length - 1"
                    :d="`M ${item.x} ${item.y + 76} L ${item.x} ${diagramData.stepItems[idx + 1].y}`"
                    :stroke="item.hasBranch ? '#10b981' : (activeStepIndex === idx ? '#3b82f6' : '#cbd5e1')"
                    :stroke-width="activeStepIndex === idx ? '2.5' : '2'"
                    :class="[
                      currentRunningOrderId && executionData?.currentStepIndex === idx
                        ? 'stroke-blue-500 animate-pulse stroke-[3]'
                        : 'dark:stroke-slate-700'
                    ]"
                    marker-end="url(#wf-arrow)"
                  />

                  <!-- True Path Condition Tag on vertical connector -->
                  <g v-if="item.hasBranch && idx < diagramData.stepItems.length - 1">
                    <rect
                      :x="item.x - 34"
                      :y="item.y + 82"
                      width="68"
                      height="18"
                      rx="9"
                      fill="#ecfdf5"
                      stroke="#10b981"
                      stroke-width="1"
                      class="dark:fill-slate-800"
                    />
                    <text
                      :x="item.x"
                      :y="item.y + 94"
                      text-anchor="middle"
                      font-size="9"
                      font-weight="bold"
                      fill="#059669"
                    >
                      ✓ 正常放行
                    </text>
                  </g>

                  <!-- Branch Curve: From Step i to Branch Card -->
                  <g v-if="item.hasBranch && item.branchX">
                    <path
                      :d="`M ${item.x + 105} ${item.y + 38} C ${item.x + 165} ${item.y + 38}, ${item.branchX - 60} ${item.branchY + 38}, ${item.branchX} ${item.branchY + 38}`"
                      stroke="#f59e0b"
                      stroke-width="2.5"
                      stroke-dasharray="5,3"
                      marker-end="url(#wf-arrow-branch)"
                    />
                    <!-- Branch Condition Tag pill on curve -->
                    <rect
                      :x="(item.x + item.branchX) / 2 - 50"
                      :y="item.y + 25"
                      width="100"
                      height="22"
                      rx="11"
                      fill="#fff"
                      stroke="#f59e0b"
                      stroke-width="1.5"
                      class="dark:fill-slate-800"
                    />
                    <text
                      :x="(item.x + item.branchX) / 2"
                      :y="item.y + 40"
                      text-anchor="middle"
                      font-size="10"
                      font-weight="600"
                      fill="#d97706"
                    >
                      {{ item.step.branchCondition?.label || '异常分支' }}
                    </text>
                  </g>
                </g>

                <!-- Line: Last Step to End Node -->
                <path
                  v-if="diagramData.stepItems.length > 0"
                  :d="`M ${diagramData.startX} ${diagramData.stepItems[diagramData.stepItems.length - 1].y + 76} L ${diagramData.startX} ${diagramData.endY}`"
                  stroke="#10b981"
                  stroke-width="2"
                  marker-end="url(#wf-arrow)"
                />
              </svg>

              <!-- Start Trigger Node -->
              <div
                class="absolute left-1/2 -translate-x-1/2 flex items-center gap-2 px-3 py-1.5 rounded-full bg-emerald-500/10 border border-emerald-500/30 text-emerald-700 dark:text-emerald-400 font-semibold text-xs shadow-xs z-10"
                :style="{ left: `${diagramData.startX}px`, top: `${diagramData.startY}px` }"
              >
                <span class="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                <span>触发：工单或手动派发</span>
              </div>

              <!-- Step Nodes -->
              <div
                v-for="item in diagramData.stepItems"
                :key="item.step.id"
                class="absolute -translate-x-1/2 transition-all cursor-pointer z-10 group"
                :style="{
                  left: `${item.x}px`,
                  top: `${item.y}px`,
                  width: '210px'
                }"
                @click="activeStepIndex = item.idx"
              >
                <!-- Step Node Card -->
                <div
                  class="rounded-xl border shadow-xs transition bg-white dark:bg-slate-850 p-2.5 relative"
                  :class="[
                    activeStepIndex === item.idx
                      ? 'border-blue-500 ring-2 ring-blue-400/40 shadow-md'
                      : 'border-slate-200 dark:border-slate-700 hover:border-slate-400'
                  ]"
                >
                  <!-- Top Input Port -->
                  <div class="absolute -top-1.5 left-1/2 -translate-x-1/2 w-2.5 h-2.5 rounded-full bg-blue-500 border-2 border-white dark:border-slate-900" />

                  <!-- Header -->
                  <div class="flex items-center justify-between gap-1.5 pb-1 border-b border-slate-100 dark:border-slate-800">
                    <div class="flex items-center gap-1.5 min-w-0">
                      <span
                        class="w-4 h-4 rounded flex items-center justify-center font-mono font-bold text-[10px] shrink-0"
                        :class="activeStepIndex === item.idx ? 'bg-blue-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'"
                      >
                        {{ item.idx + 1 }}
                      </span>
                      <span class="font-bold text-xs truncate text-slate-800 dark:text-slate-100">
                        {{ item.step.name }}
                      </span>
                    </div>
                    <NTag size="tiny" :type="getCategoryInfo(item.step.category).tagType" round>
                      {{ getCategoryInfo(item.step.category).label }}
                    </NTag>
                  </div>

                  <!-- Body -->
                  <div class="pt-1.5 flex items-center justify-between text-[11px]">
                    <div class="flex items-center gap-1 text-blue-600 dark:text-blue-400 font-mono font-semibold">
                      <div class="i-carbon-location text-xs" />
                      <span>{{ item.step.nodeId }}</span>
                    </div>
                    <div class="text-slate-400 font-mono">
                      {{ item.step.dwellSec }}s
                    </div>
                  </div>

                  <div class="text-[10px] text-slate-400 truncate mt-0.5">
                    {{ brickName(item.step.actionType) }}
                    <span v-if="item.step.cargoId" class="text-amber-500 ml-1">{{ item.step.cargoId }}</span>
                  </div>

                  <!-- Special Condition / Mutex Badges on Card -->
                  <div v-if="item.step.mutexZoneId || item.step.branchCondition || item.step.actionType === 'WAIT_IO_SIGNAL'" class="mt-1 pt-1 border-t border-slate-100 dark:border-slate-800 flex flex-wrap gap-1 text-[9px]">
                    <span v-if="item.step.mutexZoneId" class="px-1 py-0.5 rounded bg-red-500/10 text-red-600 dark:text-red-400 font-medium">
                      互斥: {{ item.step.mutexZoneId }}
                    </span>
                    <span v-if="item.step.branchCondition" class="px-1 py-0.5 rounded bg-amber-500/10 text-amber-600 dark:text-amber-400 font-medium">
                      {{ item.step.branchCondition.label }}
                    </span>
                    <span v-if="item.step.actionType === 'WAIT_IO_SIGNAL'" class="px-1 py-0.5 rounded bg-purple-500/10 text-purple-600 dark:text-purple-400 font-medium">
                      等机台信号
                    </span>
                  </div>

                  <!-- Bottom Output Port -->
                  <div class="absolute -bottom-1.5 left-1/2 -translate-x-1/2 w-2.5 h-2.5 rounded-full bg-blue-500 border-2 border-white dark:border-slate-900" />
                  <!-- Right Output Port for Branch -->
                  <div
                    v-if="item.hasBranch"
                    class="absolute top-1/2 -right-1.5 -translate-y-1/2 w-2.5 h-2.5 rounded-full bg-amber-500 border-2 border-white dark:border-slate-900"
                  />
                </div>
              </div>

              <!-- Branch Action Cards -->
              <template v-for="item in diagramData.stepItems" :key="'branch-' + item.step.id">
                <div
                  v-if="item.hasBranch && item.branchX"
                  class="absolute -translate-y-1/2 z-10"
                  :style="{
                    left: `${item.branchX}px`,
                    top: `${item.branchY + 38}px`,
                    width: '180px'
                  }"
                >
                  <div class="rounded-xl border border-amber-400/60 bg-amber-50/60 dark:bg-amber-950/20 p-2 shadow-xs">
                    <div v-if="item.step.actionType === 'DECISION_BRANCH'" class="space-y-1">
                      <div class="flex items-center justify-between text-blue-700 dark:text-blue-400 font-bold text-[11px]">
                        <span class="flex items-center gap-1"><div class="i-carbon-split text-xs" />条件智能决策</span>
                        <NTag size="tiny" type="info">分支流</NTag>
                      </div>
                      <div class="text-[10px] text-slate-600 dark:text-slate-300 font-mono truncate">
                        {{ item.step.conditionType || 'SOC_LESS_THAN' }} ({{ item.step.conditionValue || '25' }})
                      </div>
                      <div class="text-[10px] space-y-0.5 pt-1 border-t border-amber-200 dark:border-amber-900/40">
                        <div class="flex items-center justify-between text-emerald-600 dark:text-emerald-400">
                          <span>True:</span>
                          <span class="font-bold font-mono">{{ item.step.trueTargetStepId || 'STEP-10' }}</span>
                        </div>
                        <div class="flex items-center justify-between text-amber-600 dark:text-amber-400">
                          <span>False:</span>
                          <span class="font-bold font-mono">{{ item.step.falseTargetStepId || 'STEP-11' }}</span>
                        </div>
                      </div>
                    </div>
                    <div v-else>
                      <div class="flex items-center gap-1.5 text-amber-700 dark:text-amber-400 font-bold text-[11px]">
                        <div class="i-carbon-warning-alt text-xs" />
                        <span>分支转派动作</span>
                      </div>
                      <div class="text-[11px] font-semibold text-slate-700 dark:text-slate-300 mt-1">
                        {{ item.step.branchCondition?.failAction || 'AUTO_CHARGE' }}
                      </div>
                      <div class="text-[10px] text-slate-500 flex items-center justify-between mt-1">
                        <span>避难站: {{ item.step.branchCondition?.failTargetNode || fallbackStationId('charge') }}</span>
                        <NTag size="tiny" type="warning">条件转移</NTag>
                      </div>
                    </div>
                  </div>
                </div>
              </template>

              <!-- End Node -->
              <div
                class="absolute left-1/2 -translate-x-1/2 flex items-center gap-2 px-3 py-1.5 rounded-full bg-blue-500/10 border border-blue-500/30 text-blue-700 dark:text-blue-400 font-semibold text-xs shadow-xs z-10"
                :style="{ left: `${diagramData.startX}px`, top: `${diagramData.endY}px` }"
              >
                <div class="i-carbon-checkmark-filled text-xs text-blue-500" />
                <span>工序完成</span>
              </div>
            </div>
          </div>

          <!-- Mode 2: Block Runtime Matrix View -->
          <div v-else-if="viewMode === 'block'" class="space-y-4 max-h-[680px] overflow-y-auto pr-1">
            <div
              v-for="stage in blockStages"
              :key="stage.stageId"
              class="rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/40 p-3 shadow-xs"
            >
              <!-- Stage Header -->
              <div class="flex items-center justify-between pb-2 mb-2 border-b border-slate-200/80 dark:border-slate-800">
                <div class="flex items-center gap-2">
                  <span
                    class="w-3 h-3 rounded-full"
                    :style="{ backgroundColor: stage.badgeColor }"
                  />
                  <span class="font-bold text-xs text-slate-800 dark:text-slate-100">{{ stageCaption(stage.stageName) }}</span>
                  <NTag size="tiny" round :type="getCategoryInfo(stage.category).tagType">
                    {{ getCategoryInfo(stage.category).label }}
                  </NTag>
                </div>
                <div class="flex items-center gap-2 text-[11px] font-mono text-slate-400">
                  <span>阶段耗时: {{ stage.totalDwell }}s</span>
                  <span>共 {{ stage.steps.length }} 个有序工步</span>
                </div>
              </div>

              <!-- Nested Step Cards in Stage -->
              <div class="grid grid-cols-1 md:grid-cols-2 gap-2.5">
                <div
                  v-for="stWrap in stage.steps"
                  :key="stWrap.step.id"
                  class="p-2.5 rounded-lg border bg-white dark:bg-slate-850 cursor-pointer transition select-none"
                  :class="[
                    activeStepIndex === stWrap.originalIdx
                      ? 'border-blue-500 ring-2 ring-blue-400/30 shadow-xs'
                      : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'
                  ]"
                  @click="activeStepIndex = stWrap.originalIdx"
                >
                  <div class="flex items-center justify-between gap-1.5">
                    <div class="flex items-center gap-1.5 min-w-0">
                      <span
                        class="w-5 h-5 rounded flex items-center justify-center font-mono font-bold text-[10px] shrink-0"
                        :class="activeStepIndex === stWrap.originalIdx ? 'bg-blue-600 text-white' : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'"
                      >
                        {{ stWrap.originalIdx + 1 }}
                      </span>
                      <span class="font-semibold text-xs truncate">{{ stWrap.step.name }}</span>
                    </div>
                    <NTag size="tiny" :type="getStatusBadge(stWrap.step.status).type">
                      {{ getStatusBadge(stWrap.step.status).text }}
                    </NTag>
                  </div>

                  <div class="mt-1.5 flex items-center justify-between text-[11px] font-mono text-slate-500">
                    <span class="text-blue-600 font-semibold">{{ stWrap.step.nodeId }}</span>
                    <span>{{ brickName(stWrap.step.actionType) }} · {{ stWrap.step.dwellSec }} 秒</span>
                  </div>

                  <!-- Visual tags for special actions -->
                  <div v-if="stWrap.step.mutexZoneId || stWrap.step.branchCondition || stWrap.step.actionType === 'WAIT_IO_SIGNAL'" class="mt-1.5 pt-1 border-t border-slate-100 dark:border-slate-800 flex flex-wrap gap-1 text-[10px]">
                    <NTag v-if="stWrap.step.mutexZoneId" size="tiny" type="error" :bordered="false">
                      互斥: {{ stWrap.step.mutexZoneId }}
                    </NTag>
                    <NTag v-if="stWrap.step.branchCondition" size="tiny" type="warning" :bordered="false">
                      分支: {{ stWrap.step.branchCondition.label }}
                    </NTag>
                    <NTag v-if="stWrap.step.actionType === 'WAIT_IO_SIGNAL'" size="tiny" type="info" :bordered="false">
                      等机台信号
                    </NTag>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Mode 3: Sequence Container (Pipeline List) -->
          <div v-else class="space-y-2.5 max-h-[680px] overflow-y-auto pr-1">
            <div
              v-for="(step, idx) in steps"
              :key="step.id"
              class="relative rounded-xl border transition cursor-pointer select-none group"
              :class="[
                activeStepIndex === idx
                  ? 'border-blue-500 bg-blue-50/50 dark:bg-blue-950/20 shadow-md ring-1 ring-blue-400/50'
                  : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-850 hover:border-slate-300 dark:hover:border-slate-700'
              ]"
              @click="activeStepIndex = idx"
            >
              <!-- Card Content -->
              <div class="p-3">
                <div class="flex items-start justify-between gap-2">
                  <div class="flex items-center gap-2.5 min-w-0">
                    <!-- Step Number Badge -->
                    <div
                      class="w-7 h-7 rounded-lg flex items-center justify-center font-mono font-bold text-xs shrink-0"
                      :class="[
                        activeStepIndex === idx
                          ? 'bg-blue-600 text-white shadow-sm'
                          : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'
                      ]"
                    >
                      {{ idx + 1 }}
                    </div>

                    <!-- Step Title and Category -->
                    <div class="min-w-0">
                      <div class="flex items-center gap-2">
                        <span class="font-semibold text-xs text-slate-800 dark:text-slate-100 truncate">
                          {{ step.name }}
                        </span>
                        <NTag size="tiny" :type="getCategoryInfo(step.category).tagType">
                          {{ getCategoryInfo(step.category).label }}
                        </NTag>
                      </div>
                      <div class="text-[11px] text-slate-400 font-mono mt-0.5 flex items-center gap-2">
                        <span>{{ brickName(step.actionType) }}</span>
                        <span v-if="step.cargoId" class="text-amber-600 dark:text-amber-400">物料: {{ step.cargoId }}</span>
                      </div>
                    </div>
                  </div>

                  <!-- Right: Status & Station -->
                  <div class="text-right shrink-0">
                    <div class="flex items-center justify-end gap-1.5">
                      <span class="w-2 h-2 rounded-full" :class="getStatusBadge(step.status).dotClass" />
                      <NTag size="tiny" :type="getStatusBadge(step.status).type">
                        {{ getStatusBadge(step.status).text }}
                      </NTag>
                    </div>
                    <div class="text-[11px] font-mono font-semibold text-blue-600 dark:text-blue-400 mt-1">
                      站点: {{ step.nodeId }}
                    </div>
                  </div>
                </div>

                <!-- Description & Dwell time -->
                <div class="mt-2 pt-2 border-t border-slate-100 dark:border-slate-800/80 flex items-center justify-between text-[11px] text-slate-500 dark:text-slate-400">
                  <div class="truncate max-w-[280px]">
                    {{ step.desc }}
                  </div>
                  <div class="font-mono text-slate-400 shrink-0">
                    耗时: {{ step.dwellSec }}s
                  </div>
                </div>

                <!-- Quick Action Buttons Toolbar on Hover -->
                <div class="mt-2 pt-1 flex items-center justify-end gap-1 text-slate-400 opacity-80 group-hover:opacity-100 transition">
                  <button
                    class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-blue-600 text-xs"
                    title="上移工步"
                    :disabled="idx === 0"
                    @click.stop="moveStep(idx, 'up')"
                  >
                    <div class="i-carbon-arrow-up" />
                  </button>
                  <button
                    class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-blue-600 text-xs"
                    title="下移工步"
                    :disabled="idx === steps.length - 1"
                    @click.stop="moveStep(idx, 'down')"
                  >
                    <div class="i-carbon-arrow-down" />
                  </button>
                  <button
                    class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-emerald-600 text-xs"
                    title="复制工步"
                    @click.stop="duplicateStep(idx)"
                  >
                    <div class="i-carbon-copy" />
                  </button>
                  <button
                    class="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-red-600 text-xs"
                    title="删除工步"
                    @click.stop="removeStep(idx)"
                  >
                    <div class="i-carbon-trash-can" />
                  </button>
                </div>
              </div>

              <!-- Connecting Line with Arrow between steps -->
              <div
                v-if="idx < steps.length - 1"
                class="absolute -bottom-3 left-6 z-10 flex flex-col items-center"
              >
                <div class="w-1.5 h-1.5 rounded-full bg-blue-500 shadow-sm" />
              </div>
            </div>
          </div>
        </NCard>
      </div>

      <!-- Right Panel: Node Properties Inspector (4 cols) -->
      <div class="min-h-0 space-y-3 overflow-auto lg:col-span-4">
        <NCard title="工步属性" size="small" class="shadow-sm">
          <template #header-extra>
            <NTag size="tiny" type="info" round>{{ activeStep?.id || 'STEP-01' }}</NTag>
          </template>

          <NForm v-if="activeStep" :model="activeStep" label-placement="left" label-width="95" size="small" class="space-y-1">
            <!-- Basic Configuration -->
            <div class="text-[11px] font-bold text-slate-700 dark:text-slate-300 pb-1 mb-2 border-b border-slate-100 dark:border-slate-800">
              基础属性
            </div>

            <NFormItem label="工序名称">
              <NInput v-model:value="activeStep.name" />
            </NFormItem>

            <NFormItem label="节点分类">
              <NSelect
                v-model:value="activeStep.category"
                :options="[
                  { label: '导航与移动', value: 'nav' },
                  { label: '机构与工艺', value: 'action' },
                  { label: '感知与校验', value: 'verify' },
                  { label: '交管与互斥', value: 'mutex' },
                  { label: '控制与决策', value: 'control' }
                ]"
              />
            </NFormItem>

            <NFormItem label="动作">
              <NSelect v-model:value="activeStep.actionType" :options="brickOptions" />
            </NFormItem>

            <!-- Topology & Station Configuration -->
            <div class="text-[11px] font-bold text-slate-700 dark:text-slate-300 pb-1 mb-2 pt-2 border-b border-slate-100 dark:border-slate-800">
              目标工位
            </div>

            <NFormItem label="关联工位">
              <NSelect
                v-model:value="activeStep.nodeId"
                filterable
                tag
                :options="(mapStore.currentGraph?.nodes || []).map(n => ({
                  label: `${n.id} - ${n.name || n.type} (${n.x.toFixed(1)}, ${n.y.toFixed(1)})`,
                  value: n.id
                }))"
              />
            </NFormItem>

            <!-- Process & Hardware Parameters -->
            <div class="text-[11px] font-bold text-slate-700 dark:text-slate-300 pb-1 mb-2 pt-2 border-b border-slate-100 dark:border-slate-800">
              工艺参数
            </div>

            <NFormItem label="工艺耗时 (秒)">
              <NInputNumber v-model:value="activeStep.dwellSec" :min="0" :max="300" class="w-full" />
            </NFormItem>

            <NFormItem v-if="activeStep.category === 'nav'" label="最大限速 (m/s)">
              <NInputNumber v-model:value="activeStep.maxSpeed" :min="0.1" :max="3.0" :step="0.1" class="w-full" />
            </NFormItem>

            <NFormItem v-if="activeStep.category === 'action'" label="顶升行程 (mm)">
              <NInputNumber v-model:value="activeStep.liftStrokeMm" :min="0" :max="300" :step="10" class="w-full" />
            </NFormItem>

            <NFormItem label="物料/载具 RFID">
              <NInput v-model:value="activeStep.cargoId" placeholder="例如 FOUP-8801 / PLT-0044" />
            </NFormItem>

            <!-- Traffic & Mutex Parameters -->
            <div class="text-[11px] font-bold text-slate-700 dark:text-slate-300 pb-1 mb-2 pt-2 border-b border-slate-100 dark:border-slate-800">
              交管与容错
            </div>

            <NFormItem label="关联互斥区">
              <NSelect
                v-model:value="activeStep.mutexZoneId"
                filterable
                tag
                clearable
                placeholder="选择互斥防冲区"
                :options="(mapStore.currentGraph?.mutexZones || []).map(z => ({
                  label: `${z.id} (${z.name || '防冲区'})`,
                  value: z.id
                }))"
              />
            </NFormItem>

            <NFormItem label="超时熔断 (秒)">
              <NInputNumber v-model:value="activeStep.timeoutSec" :min="5" :max="120" class="w-full" />
            </NFormItem>

            <NFormItem label="运行态模拟">
              <NSelect
                v-model:value="activeStep.status"
                :options="[
                  { label: '就绪', value: 'ready' },
                  { label: '等待', value: 'waiting' },
                  { label: '受阻', value: 'blocked' },
                  { label: '故障', value: 'fault' }
                ]"
              />
            </NFormItem>

            <!-- Decision Branch Configuration -->
            <div class="text-[11px] font-bold text-slate-700 dark:text-slate-300 pb-1 mb-2 pt-2 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
              <span>条件分支</span>
              <NSwitch
                :value="activeStep.actionType === 'DECISION_BRANCH' || !!activeStep.branchCondition"
                size="small"
                @update:value="(val: boolean) => handleToggleBranch(val)"
              />
            </div>

            <!-- DECISION_BRANCH Detailed Target Steps -->
            <div v-if="activeStep.actionType === 'DECISION_BRANCH'" class="space-y-2 p-2.5 rounded-lg bg-blue-500/5 border border-blue-500/20 mb-2">
              <div class="text-[11px] font-bold text-blue-600 dark:text-blue-400 flex items-center gap-1">
                <div class="i-carbon-split" />
                <span>分支出口</span>
              </div>
              <NFormItem label="判定条件类型">
                <NSelect
                  v-model:value="activeStep.conditionType"
                  :options="[
                    { label: '电量低于阈值', value: 'SOC_LESS_THAN' },
                    { label: '电量过低', value: 'BATTERY_LOW' },
                    { label: '载具标签一致', value: 'RFID_MATCH' },
                    { label: '已载货', value: 'CARGO_LOADED' },
                    { label: '机台信号', value: 'IO_SIGNAL' }
                  ]"
                />
              </NFormItem>
              <NFormItem label="条件阈值 / 匹配值">
                <NInput v-model:value="activeStep.conditionValue" placeholder="例如: 25.0 / FOUP-8801 / 1" />
              </NFormItem>
              <NFormItem label="成立时跳到">
                <NSelect
                  v-model:value="activeStep.trueTargetStepId"
                  filterable
                  clearable
                  :options="stepTargetOptions"
                  placeholder="选择条件成立时的跳转目标工步"
                />
              </NFormItem>
              <NFormItem label="不成立时跳到">
                <NSelect
                  v-model:value="activeStep.falseTargetStepId"
                  filterable
                  clearable
                  :options="stepTargetOptions"
                  placeholder="选择条件不成立时的跳转目标工步"
                />
              </NFormItem>
            </div>

            <!-- Wait IO Signal Configuration -->
            <div v-if="activeStep.actionType === 'WAIT_IO_SIGNAL'" class="space-y-2 p-2.5 rounded-lg bg-emerald-500/5 border border-emerald-500/20 mb-2">
              <div class="text-[11px] font-bold text-emerald-600 dark:text-emerald-400 flex items-center gap-1">
                <div class="i-carbon-network-3" />
                <span>等待机台信号</span>
              </div>
              <NFormItem label="监听信号名称">
                <NSelect
                  v-model:value="activeStep.ioSignalName"
                  filterable
                  tag
                  :options="[
                    { label: '对接就绪', value: 'DOCK_READY' },
                    { label: '走廊清空', value: 'LINE_CLEAR' },
                    { label: '物料到位', value: 'MATERIAL_ARRIVED' }
                  ]"
                />
              </NFormItem>
              <NFormItem label="期望放行电平">
                <NSelect
                  v-model:value="activeStep.expectedIoValue"
                  :options="[
                    { label: '开', value: '1' },
                    { label: '关', value: '0' }
                  ]"
                />
              </NFormItem>
            </div>

            <!-- General Branch Condition for Non-Decision Steps -->
            <div v-if="activeStep.actionType !== 'DECISION_BRANCH' && activeStep.branchCondition" class="space-y-2 p-2.5 rounded-lg bg-amber-500/5 border border-amber-500/20 mb-2">
              <NFormItem label="分支触发条件">
                <NSelect
                  v-model:value="activeStep.branchCondition.type"
                  :options="[
                    { label: '低电量应急回充 (BMS SOC < 20%)', value: 'bms_low' },
                    { label: '标签校验不符', value: 'rfid_mismatch' },
                    { label: '载荷异常', value: 'cargo_weight' },
                    { label: '外部机台未就绪 (IO Busy / Fault)', value: 'io_signal' }
                  ]"
                />
              </NFormItem>
              <NFormItem label="分支提示标识">
                <NInput v-model:value="activeStep.branchCondition.label" placeholder="分支描述" />
              </NFormItem>
              <NFormItem label="分支目标站点">
                <NSelect
                  v-model:value="activeStep.branchCondition.failTargetNode"
                  filterable
                  tag
                  :options="(mapStore.currentGraph?.nodes || []).map(n => ({
                    label: `${n.id} - ${n.name || n.type}`,
                    value: n.id
                  }))"
                />
              </NFormItem>
              <NFormItem label="分支执行动作">
                <NSelect
                  v-model:value="activeStep.branchCondition.failAction"
                  :options="[
                    { label: '自动驶入快充电极 (AUTO_CHARGE)', value: 'AUTO_CHARGE' },
                    { label: '驶入侧向避让站 (PULL_OVER)', value: 'PULL_OVER' },
                    { label: '原地安全暂停 (WAIT_TIME)', value: 'WAIT_TIME' }
                  ]"
                />
              </NFormItem>
            </div>

            <!-- Terminal Step & NextStep Flow Control -->
            <div class="space-y-2 p-2 rounded-lg bg-slate-50 dark:bg-slate-800/40 border border-slate-200 dark:border-slate-700/60 mb-2">
              <div class="flex items-center justify-between">
                <span class="text-[11px] text-slate-700 dark:text-slate-300 font-medium">分支终点 / 结束工单</span>
                <NSwitch v-model:value="activeStep.isTerminal" size="small" />
              </div>
              <div class="text-[10px] text-slate-400">
                若开启，该工步完成后即归档完成任务，防止继续顺序顺延进入互斥分支。
              </div>
              <NFormItem v-if="!activeStep.isTerminal" label="后置强制跳转">
                <NSelect
                  v-model:value="activeStep.nextStepId"
                  filterable
                  clearable
                  :options="stepTargetOptions"
                  placeholder="默认顺序执行下一工步"
                />
              </NFormItem>
            </div>

            <!-- Description -->
            <NFormItem label="逻辑说明">
              <NInput v-model:value="activeStep.desc" type="textarea" :rows="2" />
            </NFormItem>
          </NForm>
        </NCard>
      </div>
    </div>

    <!-- Create Workflow Modal -->
    <NModal
      v-model:show="showCreateModal"
      preset="card"
      title="新建工序"
      style="width: 520px"
      size="small"
    >
      <div class="space-y-4 text-xs">
        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">工序名称 *</label>
          <NInput v-model:value="createForm.name" placeholder="例如：洁净间晶圆盒转运流" />
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">工序编码 (ID) *</label>
          <NInput v-model:value="createForm.id" placeholder="唯一标识，例如 wf-foup-line3" />
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">初始化策略</label>
          <NRadioGroup v-model:value="createForm.baseOn">
            <NSpace>
              <NRadio value="clone">复制当前工序步骤作为模板副本</NRadio>
              <NRadio value="blank">从空白步骤开始全新编排</NRadio>
            </NSpace>
          </NRadioGroup>
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">工艺描述</label>
          <NInput v-model:value="createForm.desc" type="textarea" :rows="2" placeholder="填写业务工艺背景与注意事项" />
        </div>
      </div>

      <template #footer>
        <div class="flex justify-end gap-2">
          <NButton size="small" @click="showCreateModal = false">取消</NButton>
          <NButton type="primary" size="small" @click="handleConfirmCreate">
            立即创建
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- Run Workflow Modal -->
    <NModal
      v-model:show="showRunModal"
      preset="card"
      title="运行工序"
      style="width: 540px"
      size="small"
    >
      <div class="space-y-4 text-xs">
        <div class="p-3 bg-blue-50 dark:bg-blue-950/20 rounded-lg text-blue-800 dark:text-blue-300 border border-blue-200 dark:border-blue-900/40">
          <div class="font-bold flex items-center gap-1.5">
            <div class="i-carbon-play-filled" />
            即将下发工序：{{ currentTemplate?.name }}
          </div>
          <div class="mt-1 text-[11px] opacity-80">
            包含 {{ steps.length }} 个有序执行工步，预计工艺总耗时 {{ steps.reduce((acc, s) => acc + (s.dwellSec || 0), 0) }} 秒。
          </div>
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">任务流水号</label>
          <NInput v-model:value="runForm.taskSerial" placeholder="自动生成或自定义" />
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">指定执行车辆</label>
          <NSelect
            v-model:value="runForm.vehicleId"
            clearable
            placeholder="默认自动选择最近闲置达标车辆"
            :options="[
              { label: '自动分配车辆', value: '' },
              ...availableVehicles
            ]"
          />
        </div>

        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">调度优先级 (1~100)</label>
          <NInputNumber v-model:value="runForm.priority" :min="1" :max="100" class="w-full" />
        </div>
      </div>

      <template #footer>
        <div class="flex justify-end gap-2">
          <NButton size="small" @click="showRunModal = false">取消</NButton>
          <NButton type="primary" size="small" @click="handleConfirmRun">
            确认下发执行
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- Live Step Execution Monitor Modal -->
    <NModal
      v-model:show="showExecutionMonitor"
      preset="card"
      title="执行进度"
      style="width: 720px"
      size="small"
    >
      <div v-if="executionData" class="space-y-4 text-xs">
        <!-- Telemetry Summary Bar -->
        <div class="p-3 bg-slate-50 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 flex flex-wrap items-center justify-between gap-3">
          <div>
            <div class="flex items-center gap-2">
              <span class="font-bold text-sm text-slate-800 dark:text-slate-100 font-mono">
                {{ executionData.orderSerial }}
              </span>
              <NTag size="small" :type="executionData.status === 'Done' ? 'success' : (executionData.status === 'Running' ? 'info' : 'warning')">
                {{ executionData.status }}
              </NTag>
            </div>
            <div class="text-[11px] text-slate-400 mt-0.5">
              创建于: {{ executionData.startedAt || '刚刚' }}
            </div>
          </div>

          <div class="flex items-center gap-4">
            <div class="text-right">
              <div class="text-slate-400 text-[10px]">进度</div>
              <div class="font-bold text-sm text-blue-600 font-mono">
                {{ Math.min(executionData.currentStepIndex + 1, executionData.totalSteps) }} / {{ executionData.totalSteps }}
              </div>
            </div>

            <div v-if="executionData.vehicle" class="p-2 bg-white dark:bg-slate-850 rounded-lg border border-slate-200 dark:border-slate-700 flex items-center gap-3">
              <div>
                <div class="font-bold text-xs">{{ executionData.vehicle.name }}</div>
                <div class="text-[10px] text-slate-400 font-mono">
                  ({{ executionData.vehicle.pose.x }}, {{ executionData.vehicle.pose.y }}) | {{ executionData.vehicle.speed }} m/s
                </div>
              </div>
              <div class="text-right">
                <span class="text-[11px] font-bold font-mono text-emerald-600">
                  {{ executionData.vehicle.battery }}%
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- Steps Timeline Progress -->
        <div class="space-y-2 max-h-[380px] overflow-y-auto pr-1">
          <div
            v-for="(st, idx) in executionData.steps"
            :key="st.stepIndex"
            class="p-2.5 rounded-lg border flex items-center justify-between transition"
            :class="[
              st.isCompleted
                ? 'border-emerald-200 dark:border-emerald-900/40 bg-emerald-50/40 dark:bg-emerald-950/10'
                : (st.isExecuting
                    ? 'border-blue-500 bg-blue-50/60 dark:bg-blue-950/20 ring-1 ring-blue-400 shadow-sm'
                    : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-850 opacity-60')
            ]"
          >
            <div class="flex items-center gap-2.5 min-w-0">
              <div
                class="w-6 h-6 rounded-full flex items-center justify-center font-bold text-xs shrink-0"
                :class="[
                  st.isCompleted
                    ? 'bg-emerald-500 text-white'
                    : (st.isExecuting
                        ? 'bg-blue-600 text-white animate-pulse'
                        : 'bg-slate-200 dark:bg-slate-700 text-slate-500')
                ]"
              >
                <div v-if="st.isCompleted" class="i-carbon-checkmark text-sm" />
                <span v-else>{{ idx + 1 }}</span>
              </div>

              <div class="truncate">
                <div class="font-semibold text-xs text-slate-800 dark:text-slate-100 flex items-center gap-1.5 truncate">
                  <span>{{ st.name }}</span>
                  <NTag size="tiny" :bordered="false">{{ st.action }}</NTag>
                </div>
                <div class="text-[10px] text-slate-400 font-mono mt-0.5">
                  工位: {{ st.targetNodeId || '当前位置' }} | 耗时: {{ st.dwellSec }}s
                  <span v-if="st.finishedAt" class="ml-2 text-emerald-600">完成于 {{ st.finishedAt }}</span>
                </div>
              </div>
            </div>

            <div class="shrink-0 text-right">
              <NTag
                size="tiny"
                :type="st.isCompleted ? 'success' : (st.isExecuting ? 'info' : 'default')"
              >
                {{ st.isCompleted ? '已完成' : (st.isExecuting ? '执行中...' : '排队中') }}
              </NTag>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="py-8 text-center text-slate-400">
        正在连接并拉取调度引擎执行态...
      </div>

      <template #footer>
        <div class="flex items-center justify-between">
          <NButton size="small" secondary @click="handleGoSimulation">
            <template #icon><div class="i-carbon-screen" /></template>
            在数字孪生视景中查看
          </NButton>

          <NSpace :size="8">
            <NButton size="small" tertiary type="warning" @click="handleSkipStep">
              <template #icon><div class="i-carbon-skip-forward" /></template>
              跳过当前工步
            </NButton>
            <NButton size="small" tertiary type="error" @click="handleCancelExecution">
              <template #icon><div class="i-carbon-close-outline" /></template>
              中止任务
            </NButton>
            <NButton size="small" @click="showExecutionMonitor = false">
              关闭窗口
            </NButton>
          </NSpace>
        </div>
      </template>
    </NModal>

    <!-- Pre-Flight Check Report Modal -->
    <NModal
      v-model:show="showPreflightModal"
      preset="card"
      title="拓扑预检"
      style="width: 540px"
      size="small"
    >
      <div v-if="preflightReport" class="space-y-3 text-xs">
        <div class="flex items-center gap-2 p-3 rounded-lg" :class="preflightReport.valid ? 'bg-emerald-50 dark:bg-emerald-950/20 text-emerald-800 dark:text-emerald-300' : 'bg-amber-50 dark:bg-amber-950/20 text-amber-800 dark:text-amber-300'">
          <div class="i-carbon-checkmark-filled text-xl text-emerald-600" v-if="preflightReport.valid" />
          <div class="i-carbon-warning-filled text-xl text-amber-600" v-else />
          <div>
            <div class="font-bold text-sm">
              {{ preflightReport.valid ? '诊断通过：工序节点与拓扑完全匹配' : '存在未标定工位或互斥区' }}
            </div>
            <div class="text-[11px] opacity-80">
              当前载入地图: {{ preflightReport.mapName }} (含 {{ preflightReport.availableNodesCount }} 个标定站点)
            </div>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded-lg">
          <div>
            <span class="text-slate-400">总工步数量:</span>
            <span class="font-bold ml-1 font-mono">{{ preflightReport.totalSteps }} 步</span>
          </div>
          <div>
            <span class="text-slate-400">工序理论总耗时:</span>
            <span class="font-bold ml-1 font-mono text-blue-600">{{ preflightReport.totalDwell }} 秒</span>
          </div>
        </div>

        <div v-if="preflightReport.missingNodes.length > 0" class="p-2.5 rounded bg-red-50 dark:bg-red-950/20 text-red-700 dark:text-red-400 border border-red-200 dark:border-red-900/40">
          <div class="font-bold mb-1">未匹配站点 (需核查地图标定):</div>
          <div class="font-mono text-[11px]">{{ preflightReport.missingNodes.join(', ') }}</div>
        </div>

        <div v-else class="text-slate-500 dark:text-slate-400 leading-relaxed">
          ✅ 所有工位站点均已在当前数字孪生拓扑中精确定位；无孤立断路死锁风险，已具备直接派发至多车协同内核执行条件。
        </div>
      </div>

      <template #footer>
        <div class="flex justify-end gap-2">
          <NButton size="small" @click="showPreflightModal = false">关闭</NButton>
          <NButton type="primary" size="small" @click="showPreflightModal = false; openRunModal()">
            前往实例化运行
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- Save Template Modal with Version & Changelog -->
    <NModal
      v-model:show="showSaveModal"
      preset="card"
      title="保存工序"
      style="width: 500px"
      size="small"
    >
      <div class="space-y-3 text-xs">
        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">工序名称</label>
          <NInput :value="currentTemplate?.name || '未命名流程'" disabled />
        </div>
        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">版本号</label>
          <NInput v-model:value="saveForm.versionTag" placeholder="例如 v1.1.0" />
        </div>
        <div>
          <label class="block font-semibold mb-1 text-slate-700 dark:text-slate-300">变更说明</label>
          <NInput v-model:value="saveForm.changeLog" type="textarea" :rows="3" placeholder="简要描述本次工步拓扑、动作或互斥区的调整内容" />
        </div>
      </div>
      <template #footer>
        <div class="flex justify-end gap-2">
          <NButton size="small" @click="showSaveModal = false">取消</NButton>
          <NButton type="primary" size="small" :loading="isSaving" @click="handleConfirmSave">
            提交并保存版本
          </NButton>
        </div>
      </template>
    </NModal>

    <!-- Version History & Diff Modal -->
    <NModal
      v-model:show="showVersionDiffModal"
      preset="card"
      title="版本历史"
      style="width: 820px"
      size="small"
    >
      <div v-if="isLoadingHistory" class="py-12 text-center text-slate-400">
        <NSpin size="small" />
        <span class="ml-2">正在拉取版本快照...</span>
      </div>

      <div v-else class="space-y-4 text-xs">
        <!-- Top bar: comparison targets & metrics -->
        <div class="p-3 bg-slate-50 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-2">
            <span class="font-bold text-slate-700 dark:text-slate-300">基准历史版本:</span>
            <NSelect
              v-model:value="selectedHistoryId"
              size="small"
              class="w-64"
              :options="historyList.map(h => ({
                label: `${h.versionTag || ('v' + h.version)} - ${h.changeLog || '工步变更'} (${h.createdAt})`,
                value: h.id
              }))"
            />
          </div>

          <div class="flex items-center gap-2">
            <NTag size="small" type="success" round>+{{ diffResults.addedCount }} 新增</NTag>
            <NTag size="small" type="error" round>-{{ diffResults.removedCount }} 删除</NTag>
            <NTag size="small" type="warning" round>~{{ diffResults.modifiedCount }} 变更</NTag>
            <NTag size="small" type="default" round>={{ diffResults.unchangedCount }} 相同</NTag>
          </div>
        </div>

        <!-- Selected History Info Summary -->
        <div v-if="selectedHistory" class="p-2.5 rounded-lg bg-blue-50/50 dark:bg-blue-950/20 border border-blue-200 dark:border-blue-900/40 text-[11px] text-blue-900 dark:text-blue-300 flex items-center justify-between">
          <div>
            <span class="font-bold mr-2">{{ selectedHistory.versionTag || ('v' + selectedHistory.version) }}</span>
            <span class="text-slate-600 dark:text-slate-300 mr-2">{{ selectedHistory.changeLog }}</span>
            <span class="text-slate-400">快照时间: {{ selectedHistory.createdAt }}</span>
          </div>
          <NButton size="tiny" type="primary" secondary @click="handleRollbackToSelectedVersion">
            <template #icon><div class="i-carbon-undo text-xs" /></template>
            应用并回滚至此版本
          </NButton>
        </div>

        <!-- Diff Items List -->
        <div class="space-y-2 max-h-[420px] overflow-y-auto pr-1">
          <div
            v-for="(diff, dIdx) in diffResults.items"
            :key="dIdx"
            class="p-2.5 rounded-lg border text-xs transition"
            :class="[
              diff.type === 'added'
                ? 'border-emerald-300 dark:border-emerald-800 bg-emerald-50/40 dark:bg-emerald-950/20'
                : (diff.type === 'removed'
                    ? 'border-red-300 dark:border-red-800 bg-red-50/40 dark:bg-red-950/20'
                    : (diff.type === 'modified'
                        ? 'border-amber-300 dark:border-amber-800 bg-amber-50/40 dark:bg-amber-950/20'
                        : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-850 opacity-70'))
            ]"
          >
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <NTag
                  size="tiny"
                  :type="diff.type === 'added' ? 'success' : (diff.type === 'removed' ? 'error' : (diff.type === 'modified' ? 'warning' : 'default'))"
                  round
                >
                  {{ diff.type === 'added' ? '+ 新增工步' : (diff.type === 'removed' ? '- 移除工步' : (diff.type === 'modified' ? '~ 参数变更' : '= 保持一致')) }}
                </NTag>
                <span class="font-bold text-slate-800 dark:text-slate-100">{{ diff.stepName }}</span>
                <span class="font-mono text-[10px] text-slate-400">{{ diff.stepId }}</span>
              </div>

              <div class="font-mono text-[11px] text-slate-400">
                <span v-if="diff.currentStep">工位: {{ diff.currentStep.nodeId }} · {{ diff.currentStep.dwellSec }}s</span>
                <span v-else-if="diff.baseStep">原工位: {{ diff.baseStep.nodeId }} · {{ diff.baseStep.dwellSec }}s</span>
              </div>
            </div>

            <div v-if="diff.diffDetails && diff.diffDetails.length > 0" class="mt-1.5 pt-1.5 border-t border-slate-200/60 dark:border-slate-700/60 text-[11px] space-y-0.5">
              <div
                v-for="(dt, dtIdx) in diff.diffDetails"
                :key="dtIdx"
                class="flex items-center gap-1.5"
                :class="diff.type === 'added' ? 'text-emerald-700 dark:text-emerald-400' : (diff.type === 'removed' ? 'text-red-700 dark:text-red-400' : 'text-amber-700 dark:text-amber-400')"
              >
                <div class="i-carbon-chevron-right text-xs shrink-0" />
                <span>{{ dt }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <div class="flex items-center justify-between">
          <span class="text-slate-400 text-[11px]">
            版本快照自动持久化于 SQLite `Fms_WorkflowTemplateHistories` 表中
          </span>
          <NSpace :size="8">
            <NButton size="small" @click="showVersionDiffModal = false">关闭窗口</NButton>
            <NButton
              v-if="selectedHistory"
              type="primary"
              size="small"
              @click="handleRollbackToSelectedVersion"
            >
              确认回滚至此版本
            </NButton>
          </NSpace>
        </div>
      </template>
    </NModal>
  </div>
</template>
