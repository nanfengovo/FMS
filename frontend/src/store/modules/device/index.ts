import { defineStore } from 'pinia';
import { ref } from 'vue';
import { useEventListener } from '@vueuse/core';
import { detectDevice, type DeviceInfo, type LayoutPreset } from '@/utils/device-detector';
import { useAppStore } from '../app';
import { useThemeStore } from '../theme';

export const useDeviceStore = defineStore('device-store', () => {
  const deviceInfo = ref<DeviceInfo>(detectDevice());
  const activeLayout = ref<LayoutPreset>(
    (localStorage.getItem('fms_layout_preset') as LayoutPreset) || deviceInfo.value.recommendedLayout
  );
  const isAutoMode = ref<boolean>(localStorage.getItem('fms_layout_auto') !== 'false');

  function updateDeviceInfo() {
    deviceInfo.value = detectDevice();
    if (isAutoMode.value) {
      applyLayout(deviceInfo.value.recommendedLayout, false);
    }
  }

  function applyLayout(preset: LayoutPreset, manual = true) {
    activeLayout.value = preset;
    if (manual) {
      isAutoMode.value = false;
      localStorage.setItem('fms_layout_auto', 'false');
    }
    localStorage.setItem('fms_layout_preset', preset);

    const appStore = useAppStore();
    const themeStore = useThemeStore();

    if (preset === 'Mobile-Handheld') {
      themeStore.setThemeLayout('vertical');
      appStore.setSiderCollapse(true);
    } else if (preset === 'Tablet-Compact') {
      themeStore.setThemeLayout('vertical');
      appStore.setSiderCollapse(true);
    } else {
      // Desktop-MultiPane
      appStore.setSiderCollapse(false);
    }
  }

  function setAutoMode(auto: boolean) {
    isAutoMode.value = auto;
    localStorage.setItem('fms_layout_auto', String(auto));
    if (auto) {
      applyLayout(deviceInfo.value.recommendedLayout, false);
    }
  }

  // Listen to window resize and orientation changes
  if (typeof window !== 'undefined') {
    useEventListener('resize', updateDeviceInfo);
    useEventListener('orientationchange', updateDeviceInfo);
  }

  return {
    deviceInfo,
    activeLayout,
    isAutoMode,
    applyLayout,
    setAutoMode,
    updateDeviceInfo
  };
});
