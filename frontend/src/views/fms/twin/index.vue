<script setup lang="ts">
import { onMounted, ref } from 'vue';
import MapCanvas from '@/components/fms/MapCanvas.vue';
import TwinViewport from '@/components/fms/TwinViewport.vue';
import FlightRecorderBar from '@/components/fms/FlightRecorderBar.vue';
import { useSimulationStore } from '@/store/modules/simulation';
import { useMapStore } from '@/store/modules/map';
import { vehicleStatusLabel } from '@/utils/fms-display';

const simStore = useSimulationStore();
const mapStore = useMapStore();
const canvasRef = ref<any>(null);
const sceneMode = ref<'3d' | '2d'>('3d');

function onSelectVehicle(id: string | null) {
  simStore.selectedVehicleId = id;
}

function onSelectNode(id: string | null) {
  mapStore.selectedNodeId = id;
}

function handleReplayFrame(frame: Fms.FlightRecorderFrame) {
  simStore.applyTelemetry(frame);
}

async function handleExitReplay() {
  await simStore.init();
}

async function injectFault(faultType: any) {
  if (simStore.selectedVehicle) {
    await simStore.injectFault(simStore.selectedVehicle.id, faultType);
  }
}

onMounted(async () => {
  await simStore.init();
  if (!mapStore.currentGraph) {
    await mapStore.loadCurrentMap();
  }
});
</script>

<template>
  <div class="relative h-[calc(100vh-164px)] min-h-0 w-full overflow-hidden rounded-2xl border border-slate-200 bg-slate-100 shadow-sm dark:border-slate-800 dark:bg-[#0c1018] dark:shadow-2xl">
    <!-- PixiJS Map Canvas (Underlay) -->
    <div class="absolute inset-0 z-0">
      <TwinViewport
        v-if="sceneMode === '3d'"
        :graph="mapStore.currentGraph"
        :vehicles="simStore.vehicles"
        :orders="simStore.liveOrders"
        :mutex-zones="simStore.mutexZones"
        :point-cloud="mapStore.pointCloud"
        :raster="mapStore.raster"
        :selected-vehicle-id="simStore.selectedVehicleId"
        :selected-node-id="mapStore.selectedNodeId"
        :selected-path-id="mapStore.selectedPathId"
        @select-vehicle="onSelectVehicle"
        @select-node="onSelectNode"
      />
      <MapCanvas
        v-else
        ref="canvasRef"
        :graph="mapStore.currentGraph"
        :vehicles="simStore.vehicles"
        :orders="simStore.liveOrders"
        :mutex-zones="simStore.mutexZones"
        :point-cloud="mapStore.pointCloud"
        :raster="mapStore.raster"
        :selected-vehicle-id="simStore.selectedVehicleId"
        :selected-node-id="mapStore.selectedNodeId"
        :selected-path-id="mapStore.selectedPathId"
        :layer-visibility="mapStore.layerVisibility"
        :hud-inset="{ bottom: 96 }"
        @select-vehicle="onSelectVehicle"
        @select-node="onSelectNode"
      />
    </div>

    <!-- Top Left: Digital Twin HUD Badge -->
    <div class="pointer-events-none absolute left-4 top-4 z-10 flex items-center gap-3">
      <div class="pointer-events-auto flex items-center gap-3.5 rounded-2xl border border-slate-200/80 dark:border-slate-700/60 bg-white/90 dark:bg-[#11151f]/90 px-4 py-2.5 shadow-xl backdrop-blur-xl transition-all">
        <div class="flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-tr from-blue-600 to-cyan-500 text-white shadow-md shadow-blue-500/25">
          <div class="i-carbon-delivery-parcel text-lg" />
        </div>
        <div>
          <div class="font-mono text-13px font-bold text-slate-900 dark:text-white flex items-center gap-2">
            <span>集群数字孪生主视景</span>
            <span class="h-2 w-2 rounded-full bg-emerald-500 animate-ping" />
          </div>
          <div class="mt-1 flex gap-1">
            <button class="rounded px-2 py-0.5 text-10px" :class="sceneMode === '3d' ? 'bg-cyan-500 text-slate-950' : 'text-slate-400'" @click="sceneMode = '3d'">3D</button>
            <button class="rounded px-2 py-0.5 text-10px" :class="sceneMode === '2d' ? 'bg-cyan-500 text-slate-950' : 'text-slate-400'" @click="sceneMode = '2d'">2D</button>
          </div>
          <div class="text-10px tracking-wider text-emerald-600 dark:text-emerald-400 font-mono font-semibold">
            运动学实时 · 10Hz
          </div>
        </div>
      </div>
    </div>

    <!-- Top Right: Live Fleet KPI Summary Bar -->
    <div class="pointer-events-none absolute right-4 top-4 z-10">
      <div class="pointer-events-auto flex items-center gap-4 rounded-2xl border border-slate-200/80 dark:border-slate-700/60 bg-white/90 dark:bg-[#11151f]/90 px-5 py-2.5 shadow-xl backdrop-blur-xl font-mono text-12px transition-all">
        <div class="flex items-center gap-1.5">
          <span class="h-2 w-2 rounded-full bg-blue-500" />
          <span class="text-11px text-slate-500 dark:text-slate-400">在线:</span>
          <span class="font-extrabold text-blue-600 dark:text-blue-400 text-13px">{{ simStore.vehicles.length }}</span>
        </div>
        <div class="h-4 w-px bg-slate-200 dark:bg-slate-700/80" />
        <div class="flex items-center gap-1.5">
          <span class="h-2 w-2 rounded-full bg-emerald-500" />
          <span class="text-11px text-slate-500 dark:text-slate-400">运行/待命:</span>
          <span class="font-extrabold text-emerald-600 dark:text-emerald-400 text-13px">{{ simStore.fleetStats.running }}/{{ simStore.fleetStats.idle }}</span>
        </div>
        <div class="h-4 w-px bg-slate-200 dark:bg-slate-700/80" />
        <div class="flex items-center gap-1.5">
          <span class="h-2 w-2 rounded-full bg-amber-500" />
          <span class="text-11px text-slate-500 dark:text-slate-400">任务:</span>
          <span class="font-extrabold text-amber-600 dark:text-amber-400 text-13px">{{ simStore.liveOrders.length }}</span>
        </div>
        <div class="h-4 w-px bg-slate-200 dark:bg-slate-700/80" />
        <div class="flex items-center gap-1.5">
          <span class="h-2 w-2 rounded-full" :class="simStore.activeConflicts.length > 0 ? 'bg-red-500 animate-ping' : 'bg-slate-400'" />
          <span class="text-11px text-slate-500 dark:text-slate-400">冲突告警:</span>
          <span class="font-extrabold text-13px" :class="simStore.activeConflicts.length > 0 ? 'text-red-600 dark:text-red-400' : 'text-slate-400'">
            {{ simStore.activeConflicts.length }}
          </span>
        </div>
      </div>
    </div>

    <!-- Bottom Center: Flight Recorder Timeline Replay Bar -->
    <div class="pointer-events-none absolute bottom-4 left-6 right-6 z-10 flex justify-center">
      <FlightRecorderBar
        @replay-frame="handleReplayFrame"
        @exit-replay="handleExitReplay"
      />
    </div>

    <!-- Right Floating Inspector (when vehicle selected) -->
    <div v-if="simStore.selectedVehicle" class="pointer-events-none absolute right-4 top-20 bottom-24 z-20 flex flex-col justify-start">
      <div class="pointer-events-auto w-84 overflow-y-auto rounded-2xl border border-blue-500/40 bg-white/95 dark:bg-[#11151f]/95 p-5 shadow-2xl backdrop-blur-xl font-mono text-12px space-y-4">
        <div class="flex items-center justify-between border-b border-slate-100 dark:border-slate-800 pb-3">
          <div class="flex items-center gap-2.5">
            <div class="flex h-8 w-8 items-center justify-center rounded-xl bg-blue-500/10 text-blue-600 dark:bg-blue-500/20 dark:text-blue-400 border border-blue-500/20">
              <div class="i-carbon-delivery-truck text-base" />
            </div>
            <div>
              <div class="text-14px font-extrabold text-slate-900 dark:text-white">{{ simStore.selectedVehicle.name || simStore.selectedVehicle.id }}</div>
              <div class="text-10px text-slate-400">{{ simStore.selectedVehicle.id }}</div>
            </div>
          </div>
          <button
            class="h-7 w-7 flex-center rounded-lg text-slate-400 hover:text-slate-700 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
            @click="simStore.selectedVehicleId = null"
          >
            <div class="i-carbon-close text-base" />
          </button>
        </div>

        <div class="space-y-2.5">
          <div class="grid grid-cols-2 gap-2 rounded-xl bg-slate-50 dark:bg-black/40 p-3 border border-slate-100 dark:border-slate-800/80">
            <div>状态: <span class="text-slate-900 dark:text-white font-bold">{{ vehicleStatusLabel(simStore.selectedVehicle.status) }}</span></div>
            <div>速度: <span class="text-emerald-600 dark:text-emerald-400 font-bold">{{ simStore.selectedVehicle.currentSpeed.toFixed(2) }} m/s</span></div>
            <div>X坐标: <span class="text-blue-600 dark:text-blue-400 font-bold">{{ simStore.selectedVehicle.pose.x.toFixed(2) }}m</span></div>
            <div>Y坐标: <span class="text-blue-600 dark:text-blue-400 font-bold">{{ simStore.selectedVehicle.pose.y.toFixed(2) }}m</span></div>
            <div class="col-span-2">航向角: <span class="text-slate-700 dark:text-slate-300">{{ ((simStore.selectedVehicle.pose.theta * 180) / Math.PI).toFixed(1) }}°</span></div>
          </div>

          <div class="rounded-xl bg-slate-50 dark:bg-black/40 p-3 border border-slate-100 dark:border-slate-800/80 space-y-1.5">
            <div class="flex justify-between text-11px">
              <span class="text-slate-500 dark:text-slate-400">电池荷电状态 (SOC):</span>
              <strong :class="simStore.selectedVehicle.batteryPercent > 20 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-500 dark:text-red-400'">
                {{ simStore.selectedVehicle.batteryPercent.toFixed(1) }}%
              </strong>
            </div>
            <div class="h-2 w-full rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
              <div
                class="h-full rounded-full transition-all duration-300"
                :class="simStore.selectedVehicle.batteryPercent > 20 ? 'bg-gradient-to-r from-emerald-500 to-teal-400' : 'bg-red-500'"
                :style="{ width: `${Math.min(100, Math.max(0, simStore.selectedVehicle.batteryPercent))}%` }"
              />
            </div>
          </div>

          <div class="flex gap-2.5 pt-1">
            <button
              class="flex-1 rounded-xl bg-gradient-to-r from-red-600 to-rose-600 py-2 font-bold text-white shadow-md shadow-red-500/20 hover:from-red-500 hover:to-rose-500 transition-all active:scale-95 text-11px flex items-center justify-center gap-1.5"
              @click="injectFault('Estop')"
            >
              <div class="i-carbon-stop-filled text-sm" />
              <span>物理急停</span>
            </button>
            <button
              class="flex-1 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-600 py-2 font-bold text-white shadow-md shadow-emerald-500/20 hover:from-emerald-500 hover:to-teal-500 transition-all active:scale-95 text-11px flex items-center justify-center gap-1.5"
              @click="injectFault('ClearFault')"
            >
              <div class="i-carbon-checkmark-filled text-sm" />
              <span>复位恢复</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
