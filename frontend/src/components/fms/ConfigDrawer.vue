<script setup lang="ts">
import { ref, watch } from 'vue';
import { fmsApi } from '@/service/api/fms';

const props = defineProps<{
  visible: boolean;
  operator?: string;
}>();

defineEmits<{
  (e: 'close'): void;
}>();

const tabs = [
  { key: 'traffic', label: '交通策略' },
  { key: 'customSet', label: 'custom.set' },
  { key: 'kcfmsXml', label: 'kcfms.xml' },
  { key: 'controlTags', label: 'control.tag' }
];

const activeTab = ref('traffic');
const loading = ref(false);
const saving = ref(false);
const statusMessage = ref('');
const statusSuccess = ref(true);

const trafficForm = ref<Fms.TrafficConfig>({
  maxSpeedLimit: 1.5,
  defaultTurningRadius: 1.0,
  deadlockTimeoutSec: 15,
  collisionSafetyRadiusMeters: 0.8,
  highDensityThreshold: 4,
  priorityWeights: {}
});

const customSetContent = ref('');
const kcfmsXmlContent = ref('');
const controlTagList = ref<{ key: string; value: string }[]>([]);

async function loadConfig() {
  loading.value = true;
  statusMessage.value = '';
  try {
    const bundle = await fmsApi.getConfigBundle();
    if (bundle.trafficConfig) {
      trafficForm.value = { ...bundle.trafficConfig };
    }
    customSetContent.value = bundle.customSet || '';
    kcfmsXmlContent.value = bundle.kcfmsXml || '';
    if (bundle.controlTags) {
      controlTagList.value = Object.entries(bundle.controlTags).map(([key, value]) => ({ key, value }));
    }
  } catch (err: any) {
    statusMessage.value = `加载配置失败: ${err.message}`;
    statusSuccess.value = false;
  } finally {
    loading.value = false;
  }
}

watch(
  () => props.visible,
  val => {
    if (val) loadConfig();
  }
);

function addControlTag() {
  controlTagList.value.push({ key: '', value: '' });
}

function showResult(success: boolean, msg: string) {
  statusSuccess.value = success;
  statusMessage.value = msg;
  setTimeout(() => {
    if (statusMessage.value === msg) statusMessage.value = '';
  }, 4000);
}

async function saveTrafficConfig() {
  saving.value = true;
  try {
    const res = await fmsApi.updateTrafficConfig(trafficForm.value, props.operator || 'Admin');
    showResult(res.success, res.message || '交通控制策略热重载成功');
  } catch (err: any) {
    showResult(false, `保存失败: ${err.message}`);
  } finally {
    saving.value = false;
  }
}

async function saveCustomSet() {
  saving.value = true;
  try {
    const res = await fmsApi.updateCustomSet(customSetContent.value, props.operator || 'Admin');
    showResult(res.success, res.message || 'custom.set 保存并热加载成功');
  } catch (err: any) {
    showResult(false, `保存失败: ${err.message}`);
  } finally {
    saving.value = false;
  }
}

async function saveKcfmsXml() {
  saving.value = true;
  try {
    const res = await fmsApi.updateKcfmsXml(kcfmsXmlContent.value, props.operator || 'Admin');
    showResult(res.success, res.message || 'kcfms.xml 保存并重载成功');
  } catch (err: any) {
    showResult(false, `保存失败: ${err.message}`);
  } finally {
    saving.value = false;
  }
}

async function saveControlTags() {
  saving.value = true;
  try {
    const record: Record<string, string> = {};
    for (const item of controlTagList.value) {
      if (item.key.trim()) record[item.key.trim()] = item.value.trim();
    }
    const res = await fmsApi.updateControlTags(record, props.operator || 'Admin');
    showResult(res.success, res.message || 'control.tag 标签集更新成功');
  } catch (err: any) {
    showResult(false, `保存失败: ${err.message}`);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <div v-if="visible" class="fixed inset-y-0 right-0 z-50 flex w-[580px] flex-col border-l border-gray-800 bg-[#12141a]/95 text-gray-200 shadow-2xl backdrop-blur-2xl transition-all">
    <!-- Header -->
    <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
      <div class="flex items-center gap-3">
        <div class="flex h-8 w-8 items-center justify-center rounded-md bg-purple-500/20 text-purple-400">
          <div class="i-carbon-settings-adjust text-lg" />
        </div>
        <div>
          <div class="font-mono text-15px font-bold text-white">配置中心 (Configuration Center)</div>
          <div class="text-11px text-gray-400">系统运行参数、交通策略与热重载</div>
        </div>
      </div>
      <button class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors" @click="$emit('close')">
        <div class="i-carbon-close text-lg" />
      </button>
    </div>

    <!-- Tabs -->
    <div class="flex border-b border-gray-800 bg-black/30 px-6 text-13px font-medium">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        class="border-b-2 px-4 py-3 transition-colors"
        :class="activeTab === tab.key ? 'border-purple-500 text-purple-400' : 'border-transparent text-gray-400 hover:text-gray-200'"
        @click="activeTab = tab.key"
      >
        {{ tab.label }}
      </button>
    </div>

    <!-- Body Content -->
    <div class="flex-1 overflow-y-auto p-6 space-y-6">
      <!-- Loading state -->
      <div v-if="loading" class="flex h-40 items-center justify-center text-gray-400">
        <div class="i-carbon-circle-dash animate-spin text-2xl mr-2" /> 正在加载系统配置...
      </div>

      <!-- Tab 1: 交通控制策略 (Traffic Config) -->
      <div v-else-if="activeTab === 'traffic'" class="space-y-5">
        <div class="rounded-lg border border-purple-500/20 bg-purple-950/20 p-3.5 text-12px text-purple-200">
          <div class="font-bold flex items-center gap-1.5 mb-1">
            <div class="i-carbon-security" /> 动态安全边界约束
          </div>
          修改交通安全参数将通过线程安全锁实时热加载至运行中调度引擎，参数超出工业安全阈值将被自动拒绝。
        </div>

        <div class="space-y-4">
          <div>
            <div class="flex justify-between text-12px font-medium text-gray-300 mb-1">
              <span>全局最高限速 (Max Speed Limit)</span>
              <span class="font-mono text-emerald-400 font-bold">{{ trafficForm.maxSpeedLimit.toFixed(2) }} m/s</span>
            </div>
            <input
              v-model.number="trafficForm.maxSpeedLimit"
              type="range"
              min="0.1"
              max="5.0"
              step="0.05"
              class="w-full accent-purple-500 cursor-pointer"
            />
            <div class="flex justify-between text-10px text-gray-500 mt-0.5">
              <span>0.1 m/s (低速蠕动)</span>
              <span>5.0 m/s (高速穿梭)</span>
            </div>
          </div>

          <div>
            <div class="flex justify-between text-12px font-medium text-gray-300 mb-1">
              <span>默认转弯曲率半径 (Default Turning Radius)</span>
              <span class="font-mono text-blue-400 font-bold">{{ trafficForm.defaultTurningRadius.toFixed(2) }} m</span>
            </div>
            <input
              v-model.number="trafficForm.defaultTurningRadius"
              type="range"
              min="0.2"
              max="5.0"
              step="0.1"
              class="w-full accent-blue-500 cursor-pointer"
            />
          </div>

          <div>
            <div class="flex justify-between text-12px font-medium text-gray-300 mb-1">
              <span>死锁预警超时阈值 (Deadlock Timeout)</span>
              <span class="font-mono text-amber-400 font-bold">{{ trafficForm.deadlockTimeoutSec }} 秒</span>
            </div>
            <input
              v-model.number="trafficForm.deadlockTimeoutSec"
              type="range"
              min="1"
              max="300"
              step="1"
              class="w-full accent-amber-500 cursor-pointer"
            />
            <div class="text-10px text-gray-500 mt-0.5">车辆停滞超过该时间触发系统告警与自动绕行策略重规划。</div>
          </div>

          <div>
            <div class="flex justify-between text-12px font-medium text-gray-300 mb-1">
              <span>碰撞包络安全半径 (Safety Radius)</span>
              <span class="font-mono text-pink-400 font-bold">{{ trafficForm.collisionSafetyRadiusMeters.toFixed(2) }} m</span>
            </div>
            <input
              v-model.number="trafficForm.collisionSafetyRadiusMeters"
              type="range"
              min="0.2"
              max="3.0"
              step="0.05"
              class="w-full accent-pink-500 cursor-pointer"
            />
          </div>

          <div>
            <div class="flex justify-between text-12px font-medium text-gray-300 mb-1">
              <span>高密度拥堵预警阈值 (High Density Threshold)</span>
              <span class="font-mono text-cyan-400 font-bold">{{ trafficForm.highDensityThreshold }} 辆</span>
            </div>
            <input
              v-model.number="trafficForm.highDensityThreshold"
              type="range"
              min="1"
              max="20"
              step="1"
              class="w-full accent-cyan-500 cursor-pointer"
            />
          </div>
        </div>

        <div class="pt-2">
          <button
            class="w-full rounded-md bg-purple-600 px-4 py-2.5 font-medium text-13px text-white shadow-lg transition-all hover:bg-purple-500 active:scale-[0.99] disabled:opacity-50"
            :disabled="saving"
            @click="saveTrafficConfig"
          >
            <span v-if="saving" class="i-carbon-circle-dash animate-spin inline-block mr-1 align-middle" />
            热更新交通控制策略
          </button>
        </div>
      </div>

      <!-- Tab 2: custom.set 接口与前缀配置 -->
      <div v-else-if="activeTab === 'customSet'" class="space-y-4">
        <div class="rounded-lg border border-gray-800 bg-black/40 p-3 text-11px text-gray-300">
          <div class="font-mono font-bold text-gray-200 mb-1">custom.set 接口回调规范说明:</div>
          <ul class="list-disc pl-4 space-y-1 text-gray-400">
            <li>包含与上位RCS通信的URL映射以及任务类型前缀规则 (如 task_arrive_target, report_robot_status 等)。</li>
            <li>支持按任务类型下划线动态路由 (例如 PA_14_get -> /PA/api/v1/xinsong/...)。</li>
          </ul>
        </div>

        <div>
          <label class="block text-12px font-mono text-gray-400 mb-1">配置文件内容 (UTF-8 Plain Text):</label>
          <textarea
            v-model="customSetContent"
            rows="14"
            class="w-full rounded-lg border border-gray-800 bg-black/60 p-3 font-mono text-12px text-emerald-400 focus:border-purple-500 focus:outline-none"
            spellcheck="false"
          ></textarea>
        </div>

        <button
          class="w-full rounded-md bg-emerald-600 px-4 py-2.5 font-medium text-13px text-white shadow-lg transition-all hover:bg-emerald-500 active:scale-[0.99] disabled:opacity-50"
          :disabled="saving"
          @click="saveCustomSet"
        >
          <span v-if="saving" class="i-carbon-circle-dash animate-spin inline-block mr-1 align-middle" />
          保存并热加载 custom.set
        </button>
      </div>

      <!-- Tab 3: kcfms.xml 核心配置 -->
      <div v-else-if="activeTab === 'kcfmsXml'" class="space-y-4">
        <div class="rounded-lg border border-gray-800 bg-black/40 p-3 text-11px text-gray-300">
          <div class="font-mono font-bold text-gray-200 mb-1">kcfms.xml 拓扑与系统参数:</div>
          <div class="text-gray-400">系统核心通信端口、心跳周期与车辆参数定义。修改将实时重解析生效。</div>
        </div>

        <div>
          <label class="block text-12px font-mono text-gray-400 mb-1">XML 格式定义:</label>
          <textarea
            v-model="kcfmsXmlContent"
            rows="14"
            class="w-full rounded-lg border border-gray-800 bg-black/60 p-3 font-mono text-12px text-blue-400 focus:border-purple-500 focus:outline-none"
            spellcheck="false"
          ></textarea>
        </div>

        <button
          class="w-full rounded-md bg-blue-600 px-4 py-2.5 font-medium text-13px text-white shadow-lg transition-all hover:bg-blue-500 active:scale-[0.99] disabled:opacity-50"
          :disabled="saving"
          @click="saveKcfmsXml"
        >
          <span v-if="saving" class="i-carbon-circle-dash animate-spin inline-block mr-1 align-middle" />
          保存并重载 kcfms.xml
        </button>
      </div>

      <!-- Tab 4: control.tag 控制标签 -->
      <div v-else-if="activeTab === 'controlTags'" class="space-y-4">
        <div class="flex items-center justify-between mb-2">
          <div class="text-12px text-gray-400">PLC/硬件控制标签映射表 (Key-Value)</div>
          <button
            class="rounded bg-purple-500/20 px-2.5 py-1 text-11px text-purple-300 hover:bg-purple-500/30"
            @click="addControlTag"
          >
            + 新增标签
          </button>
        </div>

        <div class="space-y-2">
          <div
            v-for="(item, idx) in controlTagList"
            :key="idx"
            class="flex items-center gap-2 rounded bg-black/40 p-2 border border-gray-800"
          >
            <input
              v-model="item.key"
              type="text"
              placeholder="Tag 名称 (例: AGV_ESTOP_1)"
              class="flex-1 rounded border border-gray-700 bg-black/60 px-2.5 py-1.5 font-mono text-12px text-white focus:border-purple-500 focus:outline-none"
            />
            <input
              v-model="item.value"
              type="text"
              placeholder="映射值 (例: 1)"
              class="flex-1 rounded border border-gray-700 bg-black/60 px-2.5 py-1.5 font-mono text-12px text-emerald-400 focus:border-purple-500 focus:outline-none"
            />
            <button
              class="rounded p-1.5 text-red-400 hover:bg-red-500/20"
              title="删除"
              @click="controlTagList.splice(idx, 1)"
            >
              <div class="i-carbon-trash-can text-sm" />
            </button>
          </div>
          <div v-if="controlTagList.length === 0" class="py-8 text-center text-12px text-gray-500">
            暂无控制标签，点击上方按钮添加
          </div>
        </div>

        <button
          class="w-full rounded-md bg-purple-600 px-4 py-2.5 font-medium text-13px text-white shadow-lg transition-all hover:bg-purple-500 active:scale-[0.99] disabled:opacity-50"
          :disabled="saving"
          @click="saveControlTags"
        >
          <span v-if="saving" class="i-carbon-circle-dash animate-spin inline-block mr-1 align-middle" />
          更新控制标签集 (control.tag)
        </button>
      </div>
    </div>

    <!-- Notification Toast / Status Footer -->
    <div v-if="statusMessage" class="border-t border-gray-800 bg-black/60 px-6 py-3 text-12px flex items-center justify-between" :class="statusSuccess ? 'text-emerald-400' : 'text-red-400'">
      <div class="flex items-center gap-2">
        <div :class="statusSuccess ? 'i-carbon-checkmark-filled' : 'i-carbon-warning-filled'" />
        <span>{{ statusMessage }}</span>
      </div>
      <button class="text-xs text-gray-500 hover:text-white" @click="statusMessage = ''">✕</button>
    </div>
  </div>
</template>
