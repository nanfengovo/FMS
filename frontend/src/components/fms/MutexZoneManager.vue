<script setup lang="ts">
import { computed, ref } from 'vue';
import { fmsApi } from '@/service/api/fms';
import { useAuth } from '@/hooks/business/auth';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';

const props = defineProps<{
  visible: boolean;
  operator?: string;
}>();

defineEmits<{
  (e: 'close'): void;
}>();

const { hasAuth, checkPermission } = useAuth();
const mapStore = useMapStore();
const simStore = useSimulationStore();

const targetReleaseZone = ref<Fms.MutexZoneDto | null>(null);
const releaseReason = ref('现场死锁紧急人工干预');
const releasing = ref(false);

const zones = computed<Fms.MutexZoneDto[]>(() => {
  if (simStore.mutexZones && simStore.mutexZones.length > 0) {
    return simStore.mutexZones;
  }
  return mapStore.currentGraph?.mutexZones || [];
});

function getZoneCardClass(zone: Fms.MutexZoneDto) {
  const occ = zone.currentOccupantVehicleIds?.length || 0;
  if (occ >= (zone.maxAllowedVehicles || 1)) {
    return 'border-red-500/50 bg-red-950/20';
  }
  if (occ > 0) {
    return 'border-amber-500/40 bg-amber-950/15';
  }
  return 'border-gray-800 bg-black/40';
}

function getZoneStatusBadge(zone: Fms.MutexZoneDto) {
  const occ = zone.currentOccupantVehicleIds?.length || 0;
  if (occ >= (zone.maxAllowedVehicles || 1)) {
    return 'bg-red-500/20 text-red-400 border border-red-500/40';
  }
  if (occ > 0) {
    return 'bg-amber-500/20 text-amber-400 border border-amber-500/40';
  }
  return 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/40';
}

function getZoneStatusText(zone: Fms.MutexZoneDto) {
  const occ = zone.currentOccupantVehicleIds?.length || 0;
  if (occ >= (zone.maxAllowedVehicles || 1)) return 'SATURATED 饱和锁定';
  if (occ > 0) return 'OCCUPIED 占用中';
  return 'CLEAR 空闲就绪';
}

function promptForceRelease(zone: Fms.MutexZoneDto) {
  if (!checkPermission('Fms.Traffic.ForceRelease', '强制释放交管区')) return;
  targetReleaseZone.value = zone;
}

async function executeForceRelease() {
  if (!targetReleaseZone.value) return;
  releasing.value = true;
  try {
    const res = await fmsApi.forceReleaseMutexZone(
      targetReleaseZone.value.zoneId,
      props.operator || 'Admin',
      releaseReason.value
    );
    if (res.success) {
      targetReleaseZone.value.currentOccupantVehicleIds = [];
      targetReleaseZone.value = null;
    } else {
      alert(`强制释放失败: ${res.message}`);
    }
  } catch (err: any) {
    alert(`网络错误: ${err.message}`);
  } finally {
    releasing.value = false;
  }
}
</script>

<template>
  <div v-if="visible" class="fixed inset-y-0 right-0 z-50 flex w-[560px] flex-col border-l border-gray-800 bg-[#12141a]/95 text-gray-200 shadow-2xl backdrop-blur-2xl transition-all">
    <!-- Header -->
    <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
      <div class="flex items-center gap-3">
        <div class="flex h-8 w-8 items-center justify-center rounded-md bg-red-500/20 text-red-400">
          <div class="i-carbon-road text-lg" />
        </div>
        <div>
          <div class="font-mono text-15px font-bold text-white">互斥区管控工坊 (Mutex Zone Manager)</div>
          <div class="text-11px text-gray-400">死锁隔离区、容量监视与特权强制释放</div>
        </div>
      </div>
      <button class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors" @click="$emit('close')">
        <div class="i-carbon-close text-lg" />
      </button>
    </div>

    <!-- Overview Banner -->
    <div class="border-b border-gray-800 bg-black/40 px-6 py-3 text-11px text-gray-400">
      互斥区基于射线投影 (Ray-Casting) 与拓扑节点集合双重隔离。当发生意外死锁或传感器异常时，值班员可执行紧急强制释放。
    </div>

    <!-- Zones List -->
    <div class="flex-1 overflow-y-auto p-6 space-y-4">
      <div
        v-for="zone in zones"
        :key="zone.zoneId"
        class="rounded-xl border p-4 font-mono transition-all"
        :class="getZoneCardClass(zone)"
      >
        <div class="flex items-center justify-between mb-3">
          <div class="flex items-center gap-2">
            <span class="font-mono text-14px font-bold text-white">{{ zone.name || zone.zoneId }}</span>
            <span class="text-10px text-gray-500 font-mono">({{ zone.zoneId }})</span>
          </div>
          <span
            class="rounded px-2 py-0.5 text-10px font-bold uppercase tracking-wider"
            :class="getZoneStatusBadge(zone)"
          >
            {{ getZoneStatusText(zone) }}
          </span>
        </div>

        <div class="grid grid-cols-2 gap-3 text-11px bg-black/40 rounded-lg p-3 mb-3">
          <div>
            <span class="text-gray-500">容量上限 (Max Capacity):</span>
            <span class="font-bold text-gray-200 ml-1.5">{{ zone.maxAllowedVehicles || 1 }} 辆</span>
          </div>
          <div>
            <span class="text-gray-500">当前占用数:</span>
            <span class="font-bold ml-1.5" :class="zone.currentOccupantVehicleIds?.length ? 'text-red-400' : 'text-emerald-400'">
              {{ zone.currentOccupantVehicleIds?.length || 0 }} 辆
            </span>
          </div>
          <div class="col-span-2">
            <span class="text-gray-500">成员节点集:</span>
            <span class="text-gray-300 ml-1.5">{{ zone.memberNodeIds?.join(', ') || '无' }}</span>
          </div>
          <div v-if="zone.currentOccupantVehicleIds?.length" class="col-span-2">
            <span class="text-red-400">占用车辆ID:</span>
            <span class="font-bold text-white ml-1.5">{{ zone.currentOccupantVehicleIds.join(', ') }}</span>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="flex justify-end items-center gap-2">
          <button
            class="flex items-center gap-1 rounded bg-red-600/20 border border-red-500/40 px-3 py-1.5 text-11px font-bold text-red-400 hover:bg-red-600/30 transition-colors disabled:opacity-40"
            :disabled="!hasAuth('Fms.Traffic.ForceRelease') || !zone.currentOccupantVehicleIds || zone.currentOccupantVehicleIds.length === 0"
            @click="promptForceRelease(zone)"
          >
            <div class="i-carbon-unlocked" />
            紧急强制释放 (Force Release)
          </button>
        </div>
      </div>

      <div v-if="zones.length === 0" class="py-16 text-center text-12px text-gray-500">
        当前地图未配置任何互斥区拓扑
      </div>
    </div>

    <!-- Force Release Modal Confirmation -->
    <div v-if="targetReleaseZone" class="fixed inset-0 z-60 flex items-center justify-center bg-black/80 p-4">
      <div class="w-[440px] rounded-xl border border-red-500/50 bg-[#15171e] p-5 shadow-2xl space-y-4">
        <div class="flex items-center gap-2 text-red-400 font-bold text-14px">
          <div class="i-carbon-warning-alt text-lg" />
          <span>确认执行互斥区特权强制释放？</span>
        </div>
        <p class="text-11px text-gray-300">
          目标互斥区: <strong class="text-white">{{ targetReleaseZone.name || targetReleaseZone.zoneId }}</strong><br />
          当前占用车辆: <strong class="text-amber-400">{{ targetReleaseZone.currentOccupantVehicleIds?.join(', ') }}</strong><br />
          此操作将强行清空该区段的互斥锁并记入安全审计日志。
        </p>

        <div>
          <label class="block text-11px text-gray-400 mb-1">强制释放原因 (Reason):</label>
          <input
            v-model="releaseReason"
            type="text"
            class="w-full rounded border border-gray-700 bg-black/60 px-3 py-1.5 font-mono text-12px text-white focus:border-red-500 focus:outline-none"
          />
        </div>

        <div class="flex justify-end gap-2 pt-2">
          <button
            class="rounded px-3 py-1.5 text-11px text-gray-400 hover:bg-gray-800"
            @click="targetReleaseZone = null"
          >
            取消
          </button>
          <button
            class="rounded bg-red-600 px-4 py-1.5 text-11px font-bold text-white hover:bg-red-500"
            :disabled="releasing"
            @click="executeForceRelease"
          >
            <span v-if="releasing" class="i-carbon-circle-dash animate-spin mr-1 align-middle inline-block" />
            确认释放
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
