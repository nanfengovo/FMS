/**
 * Industrial Terminal & Device Detector
 * Detects device form factor, OS, viewport resolution, DPR, and recommends layout preset.
 */

export type DeviceType = 'Desktop' | 'Tablet' | 'Mobile';
export type OperatingSystem = 'macOS' | 'Windows' | 'Linux' | 'iOS' | 'Android' | 'Unknown';
export type LayoutPreset = 'Desktop-MultiPane' | 'Tablet-Compact' | 'Mobile-Handheld';

export interface DeviceInfo {
  deviceType: DeviceType;
  os: OperatingSystem;
  screenWidth: number;
  screenHeight: number;
  viewportWidth: number;
  viewportHeight: number;
  dpr: number;
  isTouch: boolean;
  orientation: 'landscape' | 'portrait';
  recommendedLayout: LayoutPreset;
}

export function detectDevice(): DeviceInfo {
  if (typeof window === 'undefined') {
    return {
      deviceType: 'Desktop',
      os: 'Unknown',
      screenWidth: 1920,
      screenHeight: 1080,
      viewportWidth: 1920,
      viewportHeight: 1080,
      dpr: 1,
      isTouch: false,
      orientation: 'landscape',
      recommendedLayout: 'Desktop-MultiPane'
    };
  }

  const ua = navigator.userAgent || '';
  const width = window.innerWidth;
  const height = window.innerHeight;
  const isTouch = navigator.maxTouchPoints > 0 || 'ontouchstart' in window;

  // OS Detection
  let os: OperatingSystem = 'Unknown';
  if (/Macintosh|Mac OS X/i.test(ua)) os = 'macOS';
  else if (/Windows NT/i.test(ua)) os = 'Windows';
  else if (/Linux/i.test(ua) && !/Android/i.test(ua)) os = 'Linux';
  else if (/Android/i.test(ua)) os = 'Android';
  else if (/iPhone|iPad|iPod/i.test(ua)) os = 'iOS';

  // Device Form Factor
  let deviceType: DeviceType = 'Desktop';
  const isMobileUa = /Mobile|Android|iPhone|iPod|BlackBerry|IEMobile|Opera Mini/i.test(ua);
  const isTabletUa = /iPad|Tablet|PlayBook|Silk/i.test(ua) || (os === 'macOS' && isTouch && width <= 1366);

  if (isTabletUa || (width >= 768 && width <= 1024 && isTouch)) {
    deviceType = 'Tablet';
  } else if (isMobileUa || width < 768) {
    deviceType = 'Mobile';
  } else {
    deviceType = 'Desktop';
  }

  // Recommended Layout Preset
  let recommendedLayout: LayoutPreset = 'Desktop-MultiPane';
  if (deviceType === 'Mobile' || width < 768) {
    recommendedLayout = 'Mobile-Handheld';
  } else if (deviceType === 'Tablet' || width <= 1180) {
    recommendedLayout = 'Tablet-Compact';
  } else {
    recommendedLayout = 'Desktop-MultiPane';
  }

  return {
    deviceType,
    os,
    screenWidth: window.screen.width,
    screenHeight: window.screen.height,
    viewportWidth: width,
    viewportHeight: height,
    dpr: window.devicePixelRatio || 1,
    isTouch,
    orientation: width >= height ? 'landscape' : 'portrait',
    recommendedLayout
  };
}
