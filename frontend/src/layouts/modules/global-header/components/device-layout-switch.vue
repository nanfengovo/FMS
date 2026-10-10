<script setup lang="ts">
import { computed } from 'vue';
import { useDeviceStore } from '@/store/modules/device';
import type { LayoutPreset } from '@/utils/device-detector';

const deviceStore = useDeviceStore();

const currentIcon = computed(() => {
  switch (deviceStore.activeLayout) {
    case 'Mobile-Handheld':
      return 'i-carbon-mobile';
    case 'Tablet-Compact':
      return 'i-carbon-tablet';
    default:
      return 'i-carbon-screen';
  }
});

const layoutOptions: { label: string; value: LayoutPreset; desc: string; icon: string }[] = [
  {
    label: 'Desktop-MultiPane (桌面多栏)',
    value: 'Desktop-MultiPane',
    desc: '宽屏全功能展开，高数据密度与多窗交互',
    icon: 'i-carbon-screen'
  },
  {
    label: 'Tablet-Compact (平板触控)',
    value: 'Tablet-Compact',
    desc: '工控触屏/平板优化，折叠侧栏，大触控热区',
    icon: 'i-carbon-tablet'
  },
  {
    label: 'Mobile-Handheld (手持移动)',
    value: 'Mobile-Handheld',
    desc: 'PDA与手机端单栏抽屉流式响应',
    icon: 'i-carbon-mobile'
  }
];

function selectLayout(preset: LayoutPreset) {
  deviceStore.applyLayout(preset, true);
  window.$message?.info(`已切换至布局: ${preset}`);
}
</script>

<template>
  <NPopover trigger="hover" placement="bottom" :width="330">
    <template #trigger>
      <button
        class="h-9 px-2 flex items-center gap-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-600 dark:text-slate-300 transition"
        title="设备与环境自适应布局"
      >
        <div :class="currentIcon" class="text-lg text-primary" />
        <span class="text-xs font-mono font-medium hidden md:inline-block">
          {{ deviceStore.activeLayout.split('-')[0] }}
        </span>
      </button>
    </template>

    <div class="p-1 space-y-3 text-xs">
      <!-- Title & Auto Toggle -->
      <div class="flex items-center justify-between pb-2 border-b border-slate-100 dark:border-slate-800">
        <div class="flex items-center gap-1.5 font-bold text-slate-800 dark:text-slate-100">
          <div class="i-carbon-devices text-primary text-base" />
          <span>终端与环境感知引擎</span>
        </div>
        <div class="flex items-center gap-1.5">
          <span class="text-[10px] text-gray-400">自动感知</span>
          <NSwitch
            :value="deviceStore.isAutoMode"
            size="small"
            @update:value="deviceStore.setAutoMode"
          />
        </div>
      </div>

      <!-- Environment Telemetry Box -->
      <div class="p-2 bg-slate-50 dark:bg-slate-800/60 rounded border border-slate-200 dark:border-slate-700/60 font-mono space-y-1 text-[11px]">
        <div class="flex justify-between">
          <span class="text-gray-400">终端类型:</span>
          <NTag size="tiny" type="info" :bordered="false">{{ deviceStore.deviceInfo.deviceType }}</NTag>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-400">操作系统:</span>
          <span class="text-slate-700 dark:text-slate-300">{{ deviceStore.deviceInfo.os }}</span>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-400">视口分辨率:</span>
          <span class="text-slate-700 dark:text-slate-300">
            {{ deviceStore.deviceInfo.viewportWidth }} × {{ deviceStore.deviceInfo.viewportHeight }}
            (DPR {{ deviceStore.deviceInfo.dpr }})
          </span>
        </div>
        <div class="flex justify-between">
          <span class="text-gray-400">推荐布局:</span>
          <span class="text-emerald-600 dark:text-emerald-400 font-bold">
            {{ deviceStore.deviceInfo.recommendedLayout }}
          </span>
        </div>
      </div>

      <!-- Preset Selection -->
      <div class="space-y-1.5">
        <div class="font-semibold text-slate-700 dark:text-slate-300 text-[11px]">布局预设切换:</div>
        <div
          v-for="opt in layoutOptions"
          :key="opt.value"
          class="p-2 rounded border cursor-pointer transition flex items-start gap-2.5"
          :class="[
            deviceStore.activeLayout === opt.value
              ? 'border-primary bg-primary/10 text-primary'
              : 'border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/40 text-slate-700 dark:text-slate-300'
          ]"
          @click="selectLayout(opt.value)"
        >
          <div :class="opt.icon" class="text-base mt-0.5" />
          <div class="flex-1">
            <div class="font-medium text-xs flex items-center justify-between">
              <span>{{ opt.label }}</span>
              <div v-if="deviceStore.activeLayout === opt.value" class="i-carbon-checkmark text-primary text-xs" />
            </div>
            <div class="text-[10px] text-gray-400 mt-0.5">{{ opt.desc }}</div>
          </div>
        </div>
      </div>
    </div>
  </NPopover>
</template>

<style scoped>
</style>
