<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { fmsApi } from '@/service/api/fms';

defineProps<{
  visible: boolean;
  activeOperator: string;
  isFleetRunning: boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'update:activeOperator', val: string): void;
}>();

const roles = ref<Fms.RoleDto[]>([]);
const userList = ref<Fms.UserDto[]>([
  { id: '1', username: 'Admin', roles: ['R_SUPER'], isActive: true },
  { id: '2', username: 'Engineer_Zhang', roles: ['R_ENGINEER'], isActive: true },
  { id: '3', username: 'Operator_Li', roles: ['R_OPERATOR'], isActive: true },
  { id: '4', username: 'Viewer_Wang', roles: ['R_VIEWER'], isActive: true }
]);

async function loadRoles() {
  try {
    const list = await fmsApi.getRoles();
    if (list && list.length > 0) roles.value = list;
    else {
      // Fallback display
      roles.value = [
        { name: 'R_SUPER', description: '超级管理员', permissions: ['*'] },
        { name: 'R_ENGINEER', description: '系统实施与配置工程师', permissions: ['MAP_VIEW', 'MAP_MODIFY', 'CONFIG_READ', 'CONFIG_WRITE', 'DISPATCH_CONTROL'] },
        { name: 'R_OPERATOR', description: '现场调度值班员', permissions: ['MAP_VIEW', 'DISPATCH_CONTROL', 'VEHICLE_ESTOP', 'MUTEX_FORCE_RELEASE'] },
        { name: 'R_VIEWER', description: '只读监视员', permissions: ['MAP_VIEW', 'TELEMETRY_WATCH'] }
      ];
    }
  } catch {
    // defaults
  }
}

function selectOperator(name: string) {
  emit('update:activeOperator', name);
}

function getUserDescription(username: string) {
  switch (username) {
    case 'Admin': return '最高特权，可修改系统架构与参数';
    case 'Engineer_Zhang': return '地图拓扑构建与交通规则标定';
    case 'Operator_Li': return '现场急停、任务派发与互斥释放';
    case 'Viewer_Wang': return '指挥大屏与只读监控视角';
    default: return '';
  }
}

onMounted(() => {
  loadRoles();
});
</script>

<template>
  <div v-if="visible" class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-md">
    <div class="w-[720px] rounded-xl border border-gray-800 bg-[#12141a] text-gray-200 shadow-2xl overflow-hidden flex flex-col max-h-[85vh]">
      <!-- Header -->
      <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 items-center justify-center rounded-md bg-blue-500/20 text-blue-400">
            <div class="i-carbon-user-identification text-lg" />
          </div>
          <div>
            <div class="font-mono text-15px font-bold text-white">权限与安全联锁中枢 (RBAC & Safety Interlocks)</div>
            <div class="text-11px text-gray-400">分级授权、操作身份切换与运行期安全联锁保护</div>
          </div>
        </div>
        <button class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors" @click="$emit('close')">
          <div class="i-carbon-close text-lg" />
        </button>
      </div>

      <!-- Safety Interlock Banner -->
      <div class="border-b px-6 py-3" :class="isFleetRunning ? 'border-red-500/30 bg-red-950/20' : 'border-emerald-500/30 bg-emerald-950/20'">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2">
            <div :class="isFleetRunning ? 'i-carbon-locked text-red-400 animate-pulse text-lg' : 'i-carbon-unlocked text-emerald-400 text-lg'" />
            <div>
              <div class="text-12px font-bold" :class="isFleetRunning ? 'text-red-300' : 'text-emerald-300'">
                {{ isFleetRunning ? '工业安全联锁生效中 (INTERLOCK ACTIVE)' : '安全联锁就绪 (INTERLOCK READY)' }}
              </div>
              <div class="text-10px text-gray-400">
                {{ isFleetRunning ? '当前检测到车队中有运行中 (Running) 车辆，所有地图拓扑增删改操作已被底层物理拦截。' : '当前所有车辆处于待命/离线状态，允许执行拓扑维护与站点重布线。' }}
              </div>
            </div>
          </div>
          <span
            class="rounded px-2 py-0.5 font-mono text-10px font-bold uppercase tracking-wider"
            :class="isFleetRunning ? 'bg-red-500/20 text-red-400 border border-red-500/30' : 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'"
          >
            {{ isFleetRunning ? 'LOCKED' : 'PERMITTED' }}
          </span>
        </div>
      </div>

      <!-- Body Content -->
      <div class="flex-1 overflow-y-auto p-6 space-y-6">
        <!-- Current Active Operator Selector -->
        <div>
          <label class="block text-12px font-mono text-gray-400 mb-2 font-bold uppercase tracking-wider">当前会话操作者身份 (Active Operator):</label>
          <div class="grid grid-cols-4 gap-3">
            <div
              v-for="u in userList"
              :key="u.id"
              class="cursor-pointer rounded-lg border p-3 transition-all"
              :class="activeOperator === u.username ? 'border-blue-500 bg-blue-500/10 shadow-[0_0_15px_rgba(59,130,246,0.2)]' : 'border-gray-800 bg-black/40 hover:border-gray-700'"
              @click="selectOperator(u.username)"
            >
              <div class="flex items-center justify-between mb-1">
                <span class="font-mono text-13px font-bold text-white">{{ u.username }}</span>
                <span class="text-10px font-mono text-blue-400">{{ u.roles[0] }}</span>
              </div>
              <div class="text-10px text-gray-500">
                {{ getUserDescription(u.username) }}
              </div>
            </div>
          </div>
        </div>

        <!-- Role Permissions Matrix -->
        <div>
          <label class="block text-12px font-mono text-gray-400 mb-2 font-bold uppercase tracking-wider">角色授权矩阵 (Role Permissions):</label>
          <div class="space-y-3">
            <div
              v-for="role in roles"
              :key="role.name"
              class="rounded-lg border border-gray-800 bg-black/40 p-4"
            >
              <div class="flex items-center justify-between mb-2">
                <div class="flex items-center gap-2">
                  <span class="font-mono text-13px font-bold text-gray-100">{{ role.name }}</span>
                  <span class="text-11px text-gray-400">({{ role.description }})</span>
                </div>
              </div>
              <div class="flex flex-wrap gap-1.5">
                <span
                  v-for="perm in role.permissions"
                  :key="perm"
                  class="rounded bg-gray-800/80 px-2 py-0.5 font-mono text-10px text-gray-300 border border-gray-700/50"
                >
                  {{ perm }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Footer -->
      <div class="border-t border-gray-800 bg-black/40 px-6 py-3 flex justify-between items-center text-11px text-gray-400">
        <span>当前生效身份: <strong class="font-mono text-blue-400">{{ activeOperator }}</strong></span>
        <button
          class="rounded bg-blue-600 px-4 py-1.5 text-12px font-medium text-white hover:bg-blue-500 transition-colors"
          @click="$emit('close')"
        >
          确定
        </button>
      </div>
    </div>
  </div>
</template>
