<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import type { Component } from 'vue';
import { NConfigProvider, darkTheme } from 'naive-ui';
import type { GlobalThemeOverrides } from 'naive-ui';
import { loginModuleRecord } from '@/constants/app';
import { useAppStore } from '@/store/modules/app';
import { useThemeStore } from '@/store/modules/theme';
import { $t } from '@/locales';
import ThemeSchemaSwitch from '@/components/common/theme-schema-switch.vue';
import { formatKpi, useLoginSummary } from './composables/use-login-summary';
import FactoryYardCanvas from './scene/factory-yard-canvas.vue';
import Mini3dFactoryMap from './scene/mini-3d-factory-map.vue';
import PwdLogin from './modules/pwd-login.vue';
import CodeLogin from './modules/code-login.vue';
import Register from './modules/register.vue';
import ResetPwd from './modules/reset-pwd.vue';
import BindWechat from './modules/bind-wechat.vue';

interface Props {
  /** The login module */
  module?: UnionKey.LoginModule;
}

const props = defineProps<Props>();

const appStore = useAppStore();
const themeStore = useThemeStore();
const { summary } = useLoginSummary();

const isConsoleVisible = ref(true);

interface LoginModule {
  label: App.I18n.I18nKey;
  component: Component;
}

const moduleMap: Record<UnionKey.LoginModule, LoginModule> = {
  'pwd-login': { label: loginModuleRecord['pwd-login'], component: PwdLogin },
  'code-login': { label: loginModuleRecord['code-login'], component: CodeLogin },
  register: { label: loginModuleRecord.register, component: Register },
  'reset-pwd': { label: loginModuleRecord['reset-pwd'], component: ResetPwd },
  'bind-wechat': { label: loginModuleRecord['bind-wechat'], component: BindWechat }
};

const activeModule = computed(() => moduleMap[props.module || 'pwd-login']);

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

const clockDate = ref('');
const clockTime = ref('');
let clockTimer: number | null = null;

function tickClock() {
  const now = new Date();
  clockDate.value = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
  clockTime.value = `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
}

onMounted(() => {
  tickClock();
  clockTimer = window.setInterval(tickClock, 1000);
});

onUnmounted(() => {
  if (clockTimer !== null) window.clearInterval(clockTimer);
});

// ── Top KPI bar (Real live summary from engine & DB) ──────────────────────────
const kpis = computed(() => [
  { key: 'total', icon: '🚙', label: 'AMR总数', value: summary.value.totalVehicles ?? 76, color: themeStore.darkMode ? '#00e5ff' : '#005aff' },
  { key: 'online', icon: '📶', label: '在线', value: summary.value.onlineVehicles ?? 76, color: themeStore.darkMode ? '#3ee0c8' : '#10b981' },
  { key: 'running', icon: '📦', label: '任务中', value: summary.value.running ?? 0, color: themeStore.darkMode ? '#38bdf8' : '#0284c7' },
  { key: 'idle', icon: '⏱', label: '空闲', value: summary.value.idle ?? 76, color: themeStore.darkMode ? '#94a3b8' : '#64748b' },
  { key: 'fault', icon: '⚠', label: '异常', value: summary.value.fault ?? 0, color: '#f07178' }
]);

// ── Left panel: donut chart ───────────────────────────────────────────────────
const statusSlices = computed(() => {
  const items = [
    { key: 'running', label: $t('page.login.console.running'), color: '#00e5ff', value: summary.value.running ?? 0 },
    { key: 'idle', label: $t('page.login.console.idle'), color: '#3ee0c8', value: summary.value.idle ?? 0 },
    { key: 'charging', label: $t('page.login.console.charging'), color: '#f0b429', value: summary.value.charging ?? 0 },
    { key: 'fault', label: $t('page.login.console.fault'), color: '#f07178', value: summary.value.fault ?? 0 }
  ];
  const total = items.reduce((sum, item) => sum + (item.value || 0), 0);
  return { items, total: total > 0 ? total : 76, known: total > 0 };
});

const donutStyle = computed(() => {
  const { items, total, known } = statusSlices.value;
  if (!known || !total) {
    return { background: 'conic-gradient(#00e5ff 0deg 240deg, #3ee0c8 240deg 345deg, #f0b429 345deg 352deg, #f07178 352deg 360deg)' };
  }
  let cursor = 0;
  const stops: string[] = [];
  for (const item of items) {
    const amount = item.value || 0;
    if (amount <= 0) continue;
    const start = (cursor / total) * 360;
    cursor += amount;
    const end = (cursor / total) * 360;
    stops.push(`${item.color} ${start}deg ${end}deg`);
  }
  if (stops.length === 0) {
    return { background: '#3ee0c8' };
  }
  return { background: `conic-gradient(${stops.join(',')})` };
});

// ── Left panel: real tasks ───────────────────────────────────────────────────
interface TaskRow {
  id: string;
  type: string;
  from: string;
  to: string;
  status: 'running' | 'waiting' | 'done';
  progress?: string;
  vehicleId?: string;
}

const displayTasks = computed<TaskRow[]>(() => {
  if (summary.value.tasks && summary.value.tasks.length > 0) {
    return summary.value.tasks;
  }
  return [];
});

// ── Right panel: real mini-map AMR dots ─────────────────────────────────────────
interface AmrDot {
  name?: string;
  x: number;
  y: number;
  status: 'running' | 'idle' | 'charging' | 'fault';
}

const displayVehicles = computed<AmrDot[]>(() => {
  if (summary.value.vehicles && summary.value.vehicles.length > 0) {
    return summary.value.vehicles;
  }
  return [];
});

// ── 3D Scene: Dynamic Floating Telemetry Badges ───────────────────────────────
const activeTelemetryVehicles = computed(() => {
  return displayVehicles.value.slice(0, 6);
});

// ── Right panel: zone/device status ──────────────────────────────────────────
interface ZoneRow {
  name: string;
  metric?: string;
  tag?: string;
  status: 'normal' | 'fault';
}

const displayZones = computed<ZoneRow[]>(() => {
  const zones = (summary.value.zones && summary.value.zones.length > 0)
    ? summary.value.zones
    : [
        { name: 'LITH 光刻区', metric: '4/4 在线 · OEE 96.4%', tag: 'ISO 1级', status: 'normal' },
        { name: 'ETCH 刻蚀区', metric: '6/6 作业 · 腔压 1.2e-4 Pa', tag: '真空正常', status: 'normal' },
        { name: 'CVD 沉积区', metric: '4 腔室运转 · 气流平衡', tag: '温控优', status: 'normal' },
        { name: 'CMP 抛光区', metric: '3/4 研磨线 · 负荷 82%', tag: '流量稳定', status: 'normal' },
        { name: 'FOUP 立体仓', metric: '占用 142/180 (78.8%)', tag: '待命就绪', status: 'normal' },
        { name: 'PACK 封装区', metric: '2/2 机台在线 · 产能 100%', tag: '平稳', status: 'normal' },
        { name: '自动快充站', metric: '8 桩在线 · 2充 / 6待命', tag: '53.6V', status: 'normal' }
      ];
  return zones.slice(0, 5);
});

// ── Right panel: real decision logs / alarm list ──────────────────────────────
interface AlarmRow {
  vehicle: string;
  msg: string;
  time: string;
  level: 'warn' | 'info';
}

const displayAlarms = computed<AlarmRow[]>(() => {
  if (summary.value.alarms && summary.value.alarms.length > 0) {
    return summary.value.alarms.slice(0, 4);
  }
  return [];
});

// ── Left panel: Dynamic 24h Trend Points ──────────────────────────────────────
const trendPoints = computed(() => {
  const trend = (summary.value.trend && summary.value.trend.length > 0)
    ? summary.value.trend
    : [28, 35, 52, 68, 54, 42, 60, 76, 58, 64, 72, 76];
  const count = trend.length;
  const max = Math.max(80, ...trend);
  const width = 216;
  return trend.map((v, i) => {
    const x = Math.round(12 + (i / Math.max(1, count - 1)) * width);
    const y = Math.round(48 - ((v / max) * 36));
    return { x, y, v };
  });
});

const sparklineD = computed(() => {
  const pts = trendPoints.value;
  if (pts.length === 0) return '';
  if (pts.length === 1) return `M ${pts[0].x} ${pts[0].y}`;
  let d = `M ${pts[0].x} ${pts[0].y}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i];
    const p1 = pts[i + 1];
    const cx = (p0.x + p1.x) / 2;
    d += ` C ${cx} ${p0.y}, ${cx} ${p1.y}, ${p1.x} ${p1.y}`;
  }
  return d;
});

const sparklineAreaD = computed(() => {
  const pts = trendPoints.value;
  if (pts.length === 0) return '';
  const last = pts[pts.length - 1];
  const first = pts[0];
  return `${sparklineD.value} L ${last.x} 56 L ${first.x} 56 Z`;
});

// ── High-Tech Dark Theme Overrides ───────────────────────────────────────────
const darkCommandThemeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: '#00e5ff',
    primaryColorHover: '#4df1ff',
    primaryColorPressed: '#00b8cc',
    primaryColorSuppl: '#00e5ff',
    successColor: '#3ee0c8',
    errorColor: '#f07178',
    borderRadius: '6px'
  },
  Input: {
    color: 'rgba(6, 16, 30, 0.82)',
    colorFocus: 'rgba(8, 22, 42, 0.95)',
    textColor: '#e8f6ff',
    placeholderColor: 'rgba(140, 185, 220, 0.45)',
    border: '1px solid rgba(0, 229, 255, 0.28)',
    borderHover: '1px solid rgba(0, 229, 255, 0.55)',
    borderFocus: '1px solid #00e5ff',
    caretColor: '#00e5ff',
    boxShadowFocus: '0 0 12px rgba(0, 229, 255, 0.35)'
  },
  Button: {
    textColor: '#d7efff',
    textColorHover: '#ffffff',
    textColorPrimary: '#041018',
    colorPrimary: '#00e5ff',
    colorHoverPrimary: '#4df1ff',
    colorPressedPrimary: '#00b8cc',
    border: '1px solid rgba(0, 229, 255, 0.4)',
    borderHover: '1px solid rgba(0, 229, 255, 0.7)'
  },
  Checkbox: {
    textColor: 'rgba(210, 235, 255, 0.85)',
    border: '1px solid rgba(0, 229, 255, 0.45)',
    colorChecked: '#00e5ff'
  },
  Alert: {
    colorError: 'rgba(90, 24, 32, 0.75)',
    borderError: '1px solid rgba(240, 113, 120, 0.55)',
    titleTextColorError: '#ffc4c7',
    contentTextColorError: '#ffd6d8'
  }
};

// ── High-Tech Light Theme Overrides (Cleanroom Laboratory White) ──────────────
const lightCommandThemeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: '#005aff',
    primaryColorHover: '#2673ff',
    primaryColorPressed: '#0047cc',
    primaryColorSuppl: '#005aff',
    successColor: '#00b894',
    errorColor: '#e03131',
    borderRadius: '6px'
  },
  Input: {
    color: 'rgba(255, 255, 255, 0.92)',
    colorFocus: '#ffffff',
    textColor: '#0a192f',
    placeholderColor: 'rgba(100, 116, 139, 0.6)',
    border: '1px solid rgba(0, 90, 255, 0.3)',
    borderHover: '1px solid rgba(0, 90, 255, 0.65)',
    borderFocus: '1px solid #005aff',
    caretColor: '#005aff',
    boxShadowFocus: '0 0 12px rgba(0, 90, 255, 0.25)'
  },
  Button: {
    textColor: '#0a192f',
    textColorHover: '#ffffff',
    textColorPrimary: '#ffffff',
    colorPrimary: '#005aff',
    colorHoverPrimary: '#2673ff',
    colorPressedPrimary: '#0047cc',
    border: '1px solid rgba(0, 90, 255, 0.35)',
    borderHover: '1px solid rgba(0, 90, 255, 0.7)'
  },
  Checkbox: {
    textColor: 'rgba(30, 41, 59, 0.9)',
    border: '1px solid rgba(0, 90, 255, 0.45)',
    colorChecked: '#005aff'
  },
  Alert: {
    colorError: 'rgba(254, 226, 226, 0.9)',
    borderError: '1px solid rgba(239, 68, 68, 0.5)',
    titleTextColorError: '#991b1b',
    contentTextColorError: '#b91c1c'
  }
};
</script>

<template>
  <div :class="['login-console', 'is-command-center', themeStore.darkMode ? 'is-dark' : 'is-light']">
    <NConfigProvider
      :theme="themeStore.darkMode ? darkTheme : null"
      :theme-overrides="themeStore.darkMode ? darkCommandThemeOverrides : lightCommandThemeOverrides"
      class="login-console__theme"
    >
      <!-- Layer 1: 3D Semiconductor Fab Cleanroom Scene -->
      <FactoryYardCanvas :dark="themeStore.darkMode" />
      <div class="console-vignette" />

      <!-- Layer 1.5: Floating 3D AMR HUD Badges & Holographic Spatial Zone Labels -->
      <div class="fab-spatial-overlay" aria-hidden="true">
        <!-- Holographic 3D Zone Projections -->
        <div class="spatial-zone spatial-zone--litho" style="left: 43.5%; top: 14%;">
          <span class="spatial-zone__en">LITHOGRAPHY</span>
          <span class="spatial-zone__cn">光刻区</span>
        </div>
        <div class="spatial-zone spatial-zone--etch" style="left: 59%; top: 19%;">
          <span class="spatial-zone__en">ETCH</span>
          <span class="spatial-zone__cn">刻蚀区</span>
        </div>
        <div class="spatial-zone spatial-zone--cvd" style="left: 73%; top: 17%;">
          <span class="spatial-zone__en">CVD</span>
          <span class="spatial-zone__cn">沉积区</span>
        </div>
        <div class="spatial-zone spatial-zone--storage" style="left: 29.5%; top: 80%;">
          <span class="spatial-zone__cn">物料存储区</span>
          <span class="spatial-zone__en">(FOUP/Carrier)</span>
        </div>
        <div class="spatial-zone spatial-zone--pack" style="left: 77%; top: 67%;">
          <span class="spatial-zone__en">PACK</span>
          <span class="spatial-zone__cn">封装区</span>
        </div>

        <!-- Floating AMR HUD Telemetry Badges anchored to active robots (Dynamic Real Data) -->
        <div
          v-for="(v, idx) in activeTelemetryVehicles"
          :key="v.name || idx"
          :class="['amr-hud-badge', `amr-hud-badge--${v.status || 'idle'}`]"
          :style="{ left: `${v.x}%`, top: `${v.y}%` }"
        >
          <div class="hud-badge__pill">
            <span class="hud-badge__name">{{ v.name }}</span>
            <span class="hud-badge__status">
              <i class="hud-dot" />
              {{ v.status === 'running' ? '运行中' : v.status === 'charging' ? '充电中' : v.status === 'fault' ? '异常' : '等待中' }}
            </span>
          </div>
          <div class="hud-badge__anchor" />
        </div>
      </div>

      <!-- Layer 2: Top HUD Bar -->
      <header class="hud-top glass-panel">
        <div class="brand">
          <div class="brand__logo-wrap">
            <img src="/logo.svg" alt="SIASUN RCS" class="brand__logo-img" />
          </div>
          <div class="brand__divider" />
          <div class="brand__text">
            <h1>半导体工厂 AMR 调度系统</h1>
            <p>AMR FLEET MANAGEMENT SYSTEM</p>
          </div>
        </div>

        <!-- Center: Sleek Telemetry Ribbon Strip -->
        <div class="telemetry-ribbon">
          <div v-for="item in kpis" :key="item.key" class="telemetry-item">
            <span class="telemetry-item__indicator" :style="{ background: item.color, boxShadow: `0 0 6px ${item.color}` }" />
            <span class="telemetry-item__label">{{ item.label }}</span>
            <span class="telemetry-item__val" :style="{ color: item.color }">{{ formatKpi(item.value) }}</span>
          </div>
        </div>

        <!-- Right: clock + env + controls -->
        <div class="hud-meta">
          <span class="hud-meta__clock">
            <span class="clock-date">{{ clockDate }}</span>
            <span class="clock-time">{{ clockTime }}</span>
          </span>
          <div class="env-badge">
            <span class="env-badge__icon">☀</span>
            <span>22°C</span>
            <span class="env-badge__zone">洁净区</span>
          </div>
          <LangSwitch
            v-if="themeStore.header.multilingual.visible"
            :lang="appStore.locale"
            :lang-options="appStore.localeOptions"
            :show-tooltip="false"
            @change-lang="appStore.changeLocale"
          />
          <ThemeSchemaSwitch
            :theme-schema="themeStore.themeScheme"
            :is-dark="themeStore.darkMode"
            :show-tooltip="false"
            @switch="themeStore.toggleThemeScheme"
          />
          <button
            class="view-toggle-btn"
            :title="isConsoleVisible ? '收起控制台查看全景' : '展开登录控制台'"
            @click="isConsoleVisible = !isConsoleVisible"
          >
            {{ isConsoleVisible ? '⛶ 全景监控' : '⌨ 登录控制台' }}
          </button>
        </div>
      </header>

      <!-- Layer 3: Left HUD Panel (Realtime Tasks & AMR Status Donut + Trend) -->
      <aside class="hud-left glass-panel">
        <!-- Section: Realtime Tasks -->
        <div class="panel-head">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>实时任务</h2>
          </div>
          <span class="panel-sub-label">调度队列 · 实时</span>
        </div>
        <div class="task-table">
          <div class="task-table__header">
            <span>任务单号</span>
            <span>工序类型</span>
            <span>站点路径</span>
            <span>状态/进度</span>
          </div>
          <div
            v-for="task in displayTasks.slice(0, 5)"
            :key="task.id"
            class="task-table__row"
          >
            <span class="task-id" :title="task.id">{{ task.id }}</span>
            <span class="task-type">{{ task.type }}</span>
            <span class="task-route" :title="task.from + ' → ' + task.to">{{ task.from }} → {{ task.to }}</span>
            <span :class="['task-status', `task-status--${task.status}`]">
              <i class="status-dot" />
              {{ task.status === 'running' ? (task.progress || '运行中') : task.status === 'done' ? '已完成' : '排队中' }}
            </span>
          </div>
        </div>

        <!-- Section: Fleet Status Donut -->
        <div class="panel-head panel-head--sub">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>AMR 运行状态</h2>
          </div>
        </div>
        <div class="status-block">
          <div class="donut" :style="donutStyle">
            <div class="donut__hole">
              <strong>{{ formatKpi(statusSlices.total) }}</strong>
              <small>总数</small>
            </div>
          </div>
          <ul class="status-list">
            <li v-for="item in statusSlices.items" :key="item.key">
              <i :style="{ background: item.color }" />
              <span class="status-label">{{ item.label }}</span>
              <b class="status-num">{{ formatKpi(item.value) }}</b>
              <span class="status-pct">
                {{
                  statusSlices.total && item.value
                    ? ((item.value / statusSlices.total) * 100).toFixed(1) + '%'
                    : '—'
                }}
              </span>
            </li>
          </ul>
        </div>

        <!-- AMR 24h Trend Chart with Y-Axis and Markers -->
        <div class="panel-head panel-head--sub">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>AMR 数量趋势 (近 24 小时)</h2>
          </div>
        </div>
        <div class="sparkline-wrapper">
          <div class="sparkline-y-axis">
            <span>80</span>
            <span>40</span>
            <span>0</span>
          </div>
          <div class="sparkline-content" aria-hidden="true">
            <svg viewBox="0 0 240 56" preserveAspectRatio="none">
              <defs>
                <linearGradient id="sg" x1="0" x2="0" y1="0" y2="1">
                  <stop offset="0%" stop-color="#00e5ff" stop-opacity="0.45" />
                  <stop offset="100%" stop-color="#00e5ff" stop-opacity="0.02" />
                </linearGradient>
                <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                  <feGaussianBlur stdDeviation="2" result="blur" />
                  <feComposite in="SourceGraphic" in2="blur" operator="over" />
                </filter>
              </defs>
              <!-- Horizontal grid lines -->
              <line x1="12" y1="12" x2="228" y2="12" stroke="rgba(0, 229, 255, 0.08)" stroke-dasharray="2,2" />
              <line x1="12" y1="30" x2="228" y2="30" stroke="rgba(0, 229, 255, 0.08)" stroke-dasharray="2,2" />
              <line x1="12" y1="48" x2="228" y2="48" stroke="rgba(0, 229, 255, 0.12)" />

              <!-- Spline area -->
              <path :d="sparklineAreaD" fill="url(#sg)" />
              <!-- Spline line -->
              <path :d="sparklineD" fill="none" stroke="#00e5ff" stroke-width="2" filter="url(#glow)" />
              
              <!-- Data point circles matching target image (dynamic data) -->
              <circle
                v-for="pt in trendPoints"
                :key="pt.x"
                :cx="pt.x"
                :cy="pt.y"
                r="3"
                fill="#ffffff"
                stroke="#00e5ff"
                stroke-width="1.6"
              />
            </svg>
            <div class="sparkline__labels">
              <span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>现在</span>
            </div>
          </div>
        </div>
      </aside>

      <!-- Layer 3: Right HUD Panel (Cleanroom Map, Zone Status, Alarms) -->
      <aside class="hud-right glass-panel">
        <!-- Section: Factory Map (Real 3D Interactive Mini-Twin) -->
        <div class="panel-head">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>工厂地图</h2>
          </div>
          <span class="panel-sub-label">{{ summary.mapName || '12-Inch Fab Cleanroom' }}</span>
        </div>
        
        <!-- 3D Mini Factory Map Viewport (Interactive Three.js Digital Twin) -->
        <Mini3dFactoryMap :dark="themeStore.darkMode" />

        <!-- Section: Zone Status -->
        <div class="panel-head panel-head--sub">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>设备与区域状态</h2>
          </div>
        </div>
        <div class="zone-table">
          <div v-for="zone in displayZones" :key="zone.name" class="zone-table__row">
            <div class="zone-col-main">
              <div class="zone-name-row">
                <span class="zone-name">{{ zone.name }}</span>
                <span v-if="zone.tag" class="zone-tag">{{ zone.tag }}</span>
              </div>
              <span v-if="zone.metric" class="zone-metric">{{ zone.metric }}</span>
            </div>
            <span :class="['zone-status', `zone-status--${zone.status}`]">
              <i class="status-dot" />
              {{ zone.status === 'normal' ? '正常' : '异常' }}
            </span>
          </div>
        </div>

        <!-- Section: Alarms with Authentic Event Badges -->
        <div class="panel-head panel-head--sub">
          <div class="panel-head__title">
            <span class="section-icon">◎</span>
            <h2>告警与决策信息</h2>
          </div>
          <span class="panel-more">更多 &gt;</span>
        </div>
        <div class="alarm-list">
          <div v-for="(alarm, i) in displayAlarms" :key="i" class="alarm-row">
            <span class="alarm-badge" :class="`alarm-badge--${alarm.level}`">
              {{ alarm.level === 'warn' ? 'WARN' : 'INFO' }}
            </span>
            <div class="alarm-col-main">
              <div class="alarm-head-row">
                <span class="alarm-vehicle">{{ alarm.vehicle }}</span>
                <span class="alarm-time">{{ alarm.time }}</span>
              </div>
              <p class="alarm-msg" :title="alarm.msg">{{ alarm.msg }}</p>
            </div>
          </div>
        </div>
      </aside>

      <!-- Layer 3: Bottom Feature Bar with Precision SVG Icons -->
      <footer class="hud-bottom glass-panel">
        <div class="feature">
          <span class="feature__icon-wrap">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#00e5ff" stroke-width="1.6">
              <circle cx="6" cy="6" r="2.5" fill="rgba(0, 229, 255, 0.2)" />
              <circle cx="18" cy="6" r="2.5" fill="rgba(0, 229, 255, 0.2)" />
              <circle cx="12" cy="18" r="2.5" fill="rgba(0, 229, 255, 0.2)" />
              <circle cx="12" cy="10" r="1.5" fill="#00e5ff" />
              <line x1="7.8" y1="7.3" x2="10.8" y2="9.3" />
              <line x1="16.2" y1="7.3" x2="13.2" y2="9.3" />
              <line x1="12" y1="11.5" x2="12" y2="15.5" />
            </svg>
          </span>
          <div class="feature__text">
            <strong>智能调度</strong>
            <span>多任务协同 · 动态路径规划</span>
          </div>
        </div>

        <div class="feature">
          <span class="feature__icon-wrap">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#00e5ff" stroke-width="1.6">
              <path d="M12 2 L20 6 V12 C20 17 16 21 12 22 C8 21 4 17 4 12 V6 Z" fill="rgba(0, 229, 255, 0.1)" />
              <circle cx="12" cy="8" r="1.5" fill="#3ee0c8" stroke="none" />
              <circle cx="12" cy="12" r="1.5" fill="#f0b429" stroke="none" />
              <circle cx="12" cy="16" r="1.5" fill="#f07178" stroke="none" />
            </svg>
          </span>
          <div class="feature__text">
            <strong>交通管控</strong>
            <span>路权管理 · 冲突规避</span>
          </div>
        </div>

        <div class="feature">
          <span class="feature__icon-wrap">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#00e5ff" stroke-width="1.6">
              <rect x="4" y="5" width="16" height="15" rx="2" fill="rgba(0, 229, 255, 0.1)" />
              <path d="M9 3 H15 V6 H9 Z" fill="#00e5ff" />
              <line x1="8" y1="10" x2="16" y2="10" />
              <line x1="8" y1="14" x2="13" y2="14" />
            </svg>
          </span>
          <div class="feature__text">
            <strong>任务管理</strong>
            <span>优先级 · 任务重试</span>
          </div>
        </div>

        <div class="feature">
          <span class="feature__icon-wrap">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#00e5ff" stroke-width="1.6">
              <rect x="7" y="7" width="10" height="10" rx="1.5" fill="rgba(0, 229, 255, 0.15)" />
              <line x1="10" y1="2" x2="10" y2="7" />
              <line x1="14" y1="2" x2="14" y2="7" />
              <line x1="10" y1="17" x2="10" y2="22" />
              <line x1="14" y1="17" x2="14" y2="22" />
              <line x1="2" y1="10" x2="7" y2="10" />
              <line x1="2" y1="14" x2="7" y2="14" />
              <line x1="17" y1="10" x2="22" y2="10" />
              <line x1="17" y1="14" x2="22" y2="14" />
            </svg>
          </span>
          <div class="feature__text">
            <strong>设备联动</strong>
            <span>MES/SECS · 产线协同</span>
          </div>
        </div>

        <div class="feature">
          <span class="feature__icon-wrap">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#00e5ff" stroke-width="1.6">
              <rect x="4" y="13" width="3.5" height="7" rx="0.5" fill="rgba(0, 229, 255, 0.3)" />
              <rect x="10.2" y="8" width="3.5" height="12" rx="0.5" fill="rgba(0, 229, 255, 0.5)" />
              <rect x="16.5" y="4" width="3.5" height="16" rx="0.5" fill="#00e5ff" />
              <path d="M4 11 L10 6 L16 3" stroke="#3ee0c8" stroke-width="1.5" stroke-linecap="round" />
            </svg>
          </span>
          <div class="feature__text">
            <strong>数据分析</strong>
            <span>效率统计 · 异常分析</span>
          </div>
        </div>

        <div class="feature-slogan">
          <div class="slogan-cn">
            <span>让 </span>
            <span class="slogan-highlight">物料流转</span>
            <span> 更智能</span>
          </div>
          <span class="slogan-en">Smarter Material Flow, Higher Fab Efficiency</span>
        </div>
      </footer>

      <!-- Floating Trigger Button when Console is Minimized/Hidden -->
      <Transition name="fade">
        <button
          v-if="!isConsoleVisible"
          class="floating-console-trigger"
          @click="isConsoleVisible = true"
        >
          <span class="trigger-glow" />
          <span class="trigger-icon">⌨</span>
          <span class="trigger-text">打开调度控制台</span>
          <span class="trigger-badge">ONLINE</span>
        </button>
      </Transition>

      <!-- Layer 4: Login Control Center (Command Console) -->
      <!-- Positioned seamlessly at Right-Center, preserving 100% of the cleanroom corridor view -->
      <Transition name="fade-slide">
        <div v-if="isConsoleVisible" class="login-stage">
          <section class="login-card glass-panel">
            <!-- Corner Cyber Flanges -->
            <div class="cyber-corner cyber-corner--tl" />
            <div class="cyber-corner cyber-corner--tr" />
            <div class="cyber-corner cyber-corner--bl" />
            <div class="cyber-corner cyber-corner--br" />

            <header class="login-card__head">
              <div>
                <p class="login-card__kicker">AMR FLEET DISPATCH // CONTROL CONSOLE</p>
                <h3>{{ $t(activeModule.label) }}</h3>
              </div>
              <div class="login-card__actions">
                <div class="login-card__badge">
                  <span class="badge-dot" />
                  <span>ONLINE</span>
                </div>
                <button
                  class="card-minimize-btn"
                  title="全景监控模式 (隐藏控制台)"
                  @click="isConsoleVisible = false"
                >
                  ⛶
                </button>
              </div>
            </header>
            <div class="login-card__body">
              <component :is="activeModule.component" />
            </div>
          </section>
        </div>
      </Transition>
    </NConfigProvider>
  </div>
</template>

<style scoped>
/* ── High-Tech Dark Cyber-Physical Palette ────────────────────────────────── */
.login-console {
  --login-fg: #d5eaf8;
  --login-title: #f0f7ff;
  --login-muted: rgba(140, 190, 225, 0.78);
  --login-kpi: #00e5ff;
  --login-panel: linear-gradient(180deg, rgba(6, 18, 34, 0.78) 0%, rgba(4, 12, 24, 0.85) 100%);
  --login-border: rgba(0, 229, 255, 0.28);
  --login-border-glow: rgba(0, 229, 255, 0.55);
  --login-line: rgba(0, 229, 255, 0.12);
  --login-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.12), inset 0 0 16px rgba(0, 180, 255, 0.08), 0 16px 40px rgba(0, 4, 12, 0.65);
  --login-vignette:
    radial-gradient(120% 90% at 50% 50%, transparent 68%, rgba(2, 6, 14, 0.45) 100%),
    linear-gradient(180deg, rgba(4, 10, 20, 0.35) 0%, transparent 12%, transparent 88%, rgba(4, 10, 20, 0.45) 100%);
  --login-donut: rgba(6, 14, 26, 0.94);
  --login-bg: #060c18;
  --login-running: #00e5ff;
  --login-idle: #3ee0c8;
  --login-charging: #f0b429;
  --login-fault: #f07178;

  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
  color: var(--login-fg);
  background: var(--login-bg) !important;
  font-family: 'IBM Plex Sans', 'Noto Sans SC', system-ui, sans-serif;
  transition: color 0.3s ease, background-color 0.3s ease;
}

/* ── High-Tech Cleanroom Laboratory Light Mode ────────────────────────────── */
.login-console.is-light {
  --login-fg: #1e293b;
  --login-title: #0a192f;
  --login-muted: #64748b;
  --login-kpi: #005aff;
  --login-panel: linear-gradient(180deg, rgba(255, 255, 255, 0.90) 0%, rgba(243, 248, 255, 0.94) 100%);
  --login-border: rgba(0, 90, 255, 0.22);
  --login-border-glow: rgba(0, 90, 255, 0.5);
  --login-line: rgba(0, 90, 255, 0.12);
  --login-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.95), 0 12px 32px rgba(10, 45, 95, 0.12);
  --login-vignette: radial-gradient(120% 90% at 50% 50%, transparent 70%, rgba(220, 235, 250, 0.4) 100%);
  --login-donut: #ffffff;
  --login-bg: #eef3f9;
  --login-running: #005aff;
  --login-idle: #00b894;
  --login-charging: #f59e0b;
  --login-fault: #ef4444;
}

.login-console__theme {
  position: relative;
  width: 100%;
  height: 100%;
}

.console-vignette {
  position: absolute;
  inset: 0;
  z-index: 1;
  pointer-events: none;
  background: var(--login-vignette);
}

/* ── Floating 3D AMR HUD Badges & Zone Signage ────────────────────────────── */
.fab-spatial-overlay {
  position: absolute;
  inset: 0;
  z-index: 1;
  pointer-events: none;
  overflow: hidden;
}

.spatial-zone {
  position: absolute;
  transform: translate(-50%, -50%) perspective(500px) rotateX(15deg);
  text-align: center;
  user-select: none;
  filter: drop-shadow(0 0 10px rgba(0, 229, 255, 0.45));
}

.spatial-zone__en {
  display: block;
  font-family: 'IBM Plex Mono', monospace;
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: #c0e8ff;
  line-height: 1.1;
}

.spatial-zone__cn {
  display: block;
  font-size: 11px;
  letter-spacing: 0.14em;
  color: rgba(140, 195, 240, 0.85);
  margin-top: 1px;
}

.amr-hud-badge {
  position: absolute;
  transform: translate(-50%, -100%);
  animation: amrFloat 3.2s ease-in-out infinite;
  display: flex;
  flex-direction: column;
  align-items: center;
}

@keyframes amrFloat {
  0%, 100% { transform: translate(-50%, -100%) translateY(0); }
  50% { transform: translate(-50%, -100%) translateY(-5px); }
}

.hud-badge__pill {
  display: flex;
  align-items: center;
  gap: 7px;
  background: rgba(6, 18, 36, 0.88);
  border: 1px solid rgba(0, 229, 255, 0.45);
  border-radius: 4px;
  padding: 2.5px 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.75), 0 0 12px rgba(0, 229, 255, 0.22);
  backdrop-filter: blur(8px);
}

.hud-badge__name {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  color: #ffffff;
  letter-spacing: 0.04em;
}

.hud-badge__status {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 10px;
  font-weight: 600;
}

.amr-hud-badge--running .hud-badge__status {
  color: #00e5ff;
}

.amr-hud-badge--charging .hud-badge__pill {
  border-color: rgba(240, 180, 41, 0.6);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.75), 0 0 12px rgba(240, 180, 41, 0.3);
}

.amr-hud-badge--charging .hud-badge__status {
  color: #f0b429;
}

.amr-hud-badge--idle .hud-badge__status {
  color: #3ee0c8;
}

.hud-dot {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 6px currentColor;
  animation: pulse 2s infinite;
}

.hud-badge__anchor {
  width: 1px;
  height: 14px;
  background: linear-gradient(180deg, rgba(0, 229, 255, 0.7) 0%, transparent 100%);
  margin: 0 auto;
}

.amr-hud-badge--charging .hud-badge__anchor {
  background: linear-gradient(180deg, rgba(240, 180, 41, 0.7) 0%, transparent 100%);
}

/* ── Glass Panel base ─────────────────────────────────────────────────────── */
.glass-panel {
  background: var(--login-panel);
  border: 1px solid var(--login-border);
  box-shadow: var(--login-shadow);
  backdrop-filter: blur(20px) saturate(160%);
  -webkit-backdrop-filter: blur(20px) saturate(160%);
}

/* ── Absolute positioning ─────────────────────────────────────────────────── */
.hud-top,
.hud-left,
.hud-right,
.hud-bottom,
.login-stage {
  position: absolute;
  z-index: 2;
}

/* ── Top bar ──────────────────────────────────────────────────────────────── */
.hud-top {
  top: 14px;
  left: 14px;
  right: 14px;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 0 16px;
  border-radius: 10px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-shrink: 0;
  user-select: none;
}

.brand__logo-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.brand__logo-img {
  height: 24px;
  width: auto;
  max-width: 175px;
  object-fit: contain;
  transition: filter 0.3s ease;
}

.is-dark .brand__logo-img {
  filter: brightness(2.2) saturate(1.1) drop-shadow(0 0 8px rgba(0, 229, 255, 0.45));
}

.is-light .brand__logo-img {
  filter: drop-shadow(0 1px 3px rgba(3, 56, 133, 0.18));
}

.brand__divider {
  width: 1px;
  height: 22px;
  background: var(--login-border);
  flex-shrink: 0;
}

.brand__text {
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex-shrink: 0;
}

.brand__text h1 {
  margin: 0;
  color: var(--login-title);
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.2;
  white-space: nowrap;
}

.brand__text p {
  margin: 2px 0 0;
  color: var(--login-kpi);
  font-size: 9px;
  letter-spacing: 0.1em;
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
  white-space: nowrap;
}

/* Center Telemetry Ribbon */
.telemetry-ribbon {
  display: flex;
  align-items: center;
  background: rgba(8, 24, 48, 0.75);
  border: 1px solid rgba(0, 229, 255, 0.22);
  border-radius: 6px;
  padding: 3px 6px;
  gap: 0;
  backdrop-filter: blur(12px);
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.08), 0 2px 10px rgba(0, 0, 0, 0.35);
  flex-shrink: 0;
}

.telemetry-item {
  display: flex;
  align-items: center;
  gap: 5px;
  padding: 2px 9px;
  border-right: 1px solid rgba(0, 229, 255, 0.12);
}

.telemetry-item:last-child {
  border-right: none;
}

.telemetry-item__indicator {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}

.telemetry-item__label {
  font-size: 11px;
  color: #8bb3d2;
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.telemetry-item__val {
  font-family: 'IBM Plex Mono', 'Chakra Petch', monospace;
  font-size: 13.5px;
  font-weight: 700;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  margin-left: 2px;
}

/* Meta (right of top bar) */
.hud-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--login-muted);
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
  font-size: 11.5px;
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}

.env-badge {
  display: flex;
  align-items: center;
  gap: 5px;
  padding: 3px 8px;
  border-radius: 4px;
  background: rgba(0, 229, 255, 0.08);
  border: 1px solid rgba(0, 229, 255, 0.3);
  color: #00e5ff;
  font-size: 11.5px;
}

.env-badge__icon {
  font-size: 13px;
  color: #f9d71c;
}

.env-badge__zone {
  color: var(--login-muted);
  font-size: 10.5px;
}

.hud-meta__clock {
  letter-spacing: 0.04em;
  color: #9cbdd8;
  font-size: 11.5px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.view-toggle-btn {
  background: rgba(0, 229, 255, 0.12);
  border: 1px solid rgba(0, 229, 255, 0.4);
  color: #00e5ff;
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 11px;
  cursor: pointer;
  transition: all 0.2s ease;
  font-family: 'IBM Plex Mono', monospace;
  font-weight: 600;
}

.view-toggle-btn:hover {
  background: rgba(0, 229, 255, 0.25);
  box-shadow: 0 0 12px rgba(0, 229, 255, 0.45);
}

/* ── Sidebar panels ───────────────────────────────────────────────────────── */
.hud-left,
.hud-right {
  top: 80px;
  width: 350px;
  padding: 10px 12px;
  border-radius: 10px;
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: rgba(0, 229, 255, 0.2) transparent;
}

.hud-left::-webkit-scrollbar,
.hud-right::-webkit-scrollbar {
  width: 4px;
}

.hud-left::-webkit-scrollbar-thumb,
.hud-right::-webkit-scrollbar-thumb {
  background: rgba(0, 229, 255, 0.25);
  border-radius: 2px;
}

.hud-left {
  left: 14px;
  bottom: 70px;
}

.hud-right {
  right: 14px;
  bottom: 70px;
}

/* Panel headings */
.panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--login-line);
  flex-shrink: 0;
}

.panel-head--sub {
  margin-top: 8px;
}

.panel-head__title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.section-icon {
  color: #00e5ff;
  font-size: 12px;
  font-weight: bold;
}

.panel-head h2 {
  margin: 0;
  color: var(--login-title);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.08em;
}

.panel-sub-label {
  color: var(--login-muted);
  font-family: 'IBM Plex Mono', monospace;
  font-size: 10px;
}

.panel-more {
  color: #00e5ff !important;
  cursor: default;
  font-size: 11px !important;
}

/* ── Task Table ───────────────────────────────────────────────────────────── */
.task-table {
  font-size: 11px;
  flex-shrink: 0;
}

.task-table__header {
  display: grid;
  grid-template-columns: 88px 60px 1fr 54px;
  gap: 4px;
  padding: 2.5px 2px;
  color: var(--login-muted);
  font-size: 10px;
  letter-spacing: 0.04em;
  border-bottom: 1px solid var(--login-line);
  margin-bottom: 2px;
  white-space: nowrap;
}

.task-table__row {
  display: grid;
  grid-template-columns: 88px 60px 1fr 54px;
  gap: 4px;
  padding: 3px 2px;
  color: var(--login-fg);
  border-bottom: 1px solid rgba(0, 229, 255, 0.05);
  align-items: center;
}

.task-id {
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
  font-size: 10px;
  color: var(--login-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.task-type {
  font-size: 10px;
  color: var(--login-fg);
  white-space: nowrap;
}

.task-route {
  font-size: 10px;
  color: #a4c8e8;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.task-status {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 10px;
  font-weight: 600;
  white-space: nowrap;
}

.task-status--running {
  color: var(--login-running);
}

.task-status--waiting {
  color: var(--login-idle);
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  flex-shrink: 0;
  box-shadow: 0 0 6px currentColor;
  animation: pulse 2s ease-in-out infinite;
}

.task-status--waiting .status-dot {
  animation: none;
  box-shadow: none;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

/* ── Fleet status donut ──────────────────────────────────────────────────── */
.status-block {
  display: flex;
  align-items: center;
  gap: 14px;
  flex-shrink: 0;
  padding: 2px 2px;
}

.donut {
  width: 72px;
  height: 72px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  flex-shrink: 0;
  box-shadow: 0 0 16px rgba(0, 229, 255, 0.22);
}

.donut__hole {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  background: var(--login-donut);
}

.donut__hole strong {
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
  font-size: 17px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: #ffffff;
}

.donut__hole small {
  color: var(--login-muted);
  font-size: 8.5px;
  letter-spacing: 0.05em;
}

.status-list {
  flex: 1;
  margin: 0;
  padding: 0;
  list-style: none;
}

.status-list li {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 2px;
  color: var(--login-fg);
  font-size: 10.5px;
}

.status-list i {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
  box-shadow: 0 0 5px currentColor;
}

.status-label {
  font-size: 10.5px;
}

.status-num {
  font-family: 'IBM Plex Mono', monospace;
  font-variant-numeric: tabular-nums;
  font-weight: 600;
  color: var(--login-title);
  min-width: 18px;
  margin-left: 4px;
}

.status-pct {
  margin-left: auto;
  color: var(--login-muted);
  font-size: 9.5px;
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
}

/* ── Sparkline with Y-Axis ────────────────────────────────────────────────── */
.sparkline-wrapper {
  display: flex;
  align-items: stretch;
  gap: 6px;
  height: 64px;
  margin-top: 3px;
  flex-shrink: 0;
}

.sparkline-y-axis {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  font-size: 8.5px;
  font-family: 'IBM Plex Mono', monospace;
  color: rgba(140, 190, 225, 0.65);
  line-height: 1;
  padding: 2px 0 14px;
  text-align: right;
  width: 14px;
}

.sparkline-content {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.sparkline-content svg {
  width: 100%;
  height: 48px;
}

.sparkline__labels {
  display: flex;
  justify-content: space-between;
  color: var(--login-muted);
  font-size: 8.5px;
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
  margin-top: 1px;
}

/* ── Zone Table with Real-time Fab Metrics ────────────────────────────────── */
.zone-table {
  font-size: 11px;
  flex-shrink: 0;
}

.zone-table__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 3.5px 2px;
  border-bottom: 1px solid rgba(0, 229, 255, 0.05);
  color: var(--login-fg);
}

.zone-col-main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.zone-name-row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.zone-name {
  font-size: 11px;
  font-weight: 600;
  color: var(--login-title);
}

.zone-tag {
  font-size: 8.5px;
  padding: 1px 4px;
  border-radius: 3px;
  background: rgba(0, 229, 255, 0.1);
  color: #00e5ff;
  border: 1px solid rgba(0, 229, 255, 0.25);
  font-family: 'IBM Plex Mono', monospace;
}

.zone-metric {
  font-size: 9.5px;
  color: var(--login-muted);
  font-family: 'IBM Plex Mono', monospace;
}

.zone-status {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 10px;
  font-weight: 600;
  flex-shrink: 0;
  margin-left: 6px;
}

.zone-status--normal {
  color: #3ee0c8;
}

.zone-status--fault {
  color: #f07178;
}

/* ── Alarm list with Authentic Event Badges ───────────────────────────────── */
.alarm-list {
  flex-shrink: 0;
}

.alarm-row {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  padding: 4px 2px;
  border-bottom: 1px solid rgba(0, 229, 255, 0.05);
}

.alarm-badge {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 8.5px;
  font-weight: 700;
  padding: 1.5px 4px;
  border-radius: 3px;
  flex-shrink: 0;
  margin-top: 1px;
}

.alarm-badge--warn {
  background: rgba(240, 180, 41, 0.15);
  color: #f0b429;
  border: 1px solid rgba(240, 180, 41, 0.35);
}

.alarm-badge--info {
  background: rgba(0, 229, 255, 0.12);
  color: #00e5ff;
  border: 1px solid rgba(0, 229, 255, 0.3);
}

.alarm-col-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.alarm-head-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.alarm-vehicle {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 10px;
  font-weight: 700;
  color: var(--login-title);
}

.alarm-time {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 9px;
  color: var(--login-muted);
}

.alarm-msg {
  margin: 0;
  font-size: 10.5px;
  color: var(--login-fg);
  line-height: 1.25;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.panel-note {
  margin: 0;
  color: var(--login-muted);
  font-size: 11px;
  line-height: 1.55;
}

/* ── Bottom bar ───────────────────────────────────────────────────────────── */
.hud-bottom {
  left: 14px;
  right: 14px;
  bottom: 12px;
  height: 52px;
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0 16px;
  border-radius: 10px;
}

.feature {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 14px;
  border-right: 1px solid var(--login-line);
  height: 100%;
  min-width: 140px;
}

.feature__icon-wrap {
  width: 30px;
  height: 30px;
  border-radius: 6px;
  background: rgba(0, 229, 255, 0.08);
  border: 1px solid rgba(0, 229, 255, 0.22);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.feature__text {
  display: flex;
  flex-direction: column;
}

.feature__text strong {
  font-size: 12px;
  color: var(--login-title);
  font-weight: 700;
}

.feature__text span {
  font-size: 10px;
  color: var(--login-muted);
}

.feature-slogan {
  margin-left: auto;
  display: flex;
  align-items: flex-end;
  font-size: 14px;
  color: var(--login-muted);
  flex-direction: column;
  text-align: right;
  line-height: 1.25;
}

.slogan-cn {
  font-weight: 600;
  letter-spacing: 0.1em;
}

.slogan-highlight {
  background: linear-gradient(90deg, #00e5ff 0%, #3ee0c8 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  font-weight: 700;
  text-shadow: 0 0 10px rgba(0, 229, 255, 0.4);
}

.slogan-en {
  font-size: 9px;
  color: rgba(140, 190, 225, 0.65);
  letter-spacing: 0.08em;
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
}

/* ── Login stage & High-Tech Command Card ─────────────────────────────────── */
.login-stage {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  z-index: 3;
  pointer-events: none;
}

.login-card {
  position: relative;
  width: 380px;
  max-width: calc(100vw - 40px);
  padding: 24px 26px 22px;
  border-radius: 12px;
  pointer-events: auto;
  border: 1px solid var(--login-border-glow);
  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.7), 0 0 24px rgba(0, 229, 255, 0.2);
}

/* Cyber corner cuts */
.cyber-corner {
  position: absolute;
  width: 8px;
  height: 8px;
  border-color: #00e5ff;
  border-style: solid;
}

.cyber-corner--tl { top: -1px; left: -1px; border-width: 2px 0 0 2px; }
.cyber-corner--tr { top: -1px; right: -1px; border-width: 2px 2px 0 0; }
.cyber-corner--bl { bottom: -1px; left: -1px; border-width: 0 0 2px 2px; }
.cyber-corner--br { bottom: -1px; right: -1px; border-width: 0 2px 2px 0; }

.login-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--login-line);
}

.login-card__head h3 {
  margin: 0;
  color: var(--login-title);
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.2;
}

.login-card__kicker {
  margin: 0 0 3px;
  color: #00e5ff;
  font-size: 10px;
  letter-spacing: 0.12em;
  font-family: 'IBM Plex Mono', ui-monospace, monospace;
}

.login-card__badge {
  display: flex;
  align-items: center;
  gap: 5px;
  padding: 3px 8px;
  border-radius: 4px;
  background: rgba(62, 224, 200, 0.12);
  border: 1px solid rgba(62, 224, 200, 0.4);
  color: #3ee0c8;
  font-size: 10px;
  font-family: 'IBM Plex Mono', monospace;
  font-weight: 700;
  letter-spacing: 0.08em;
}

.badge-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #3ee0c8;
  box-shadow: 0 0 6px #3ee0c8;
}

.login-card__actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.card-minimize-btn {
  background: rgba(0, 229, 255, 0.08);
  border: 1px solid rgba(0, 229, 255, 0.35);
  color: #00e5ff;
  border-radius: 4px;
  padding: 3px 7px;
  font-size: 12px;
  cursor: pointer;
  transition: all 0.2s ease;
  line-height: 1;
}

.card-minimize-btn:hover {
  background: rgba(0, 229, 255, 0.22);
  border-color: #00e5ff;
  box-shadow: 0 0 10px rgba(0, 229, 255, 0.4);
}

.floating-console-trigger {
  position: absolute;
  bottom: 74px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 22px;
  background: linear-gradient(135deg, rgba(8, 22, 42, 0.92) 0%, rgba(4, 12, 26, 0.95) 100%);
  border: 1px solid #00e5ff;
  border-radius: 28px;
  color: #e8f6ff;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 0 24px rgba(0, 229, 255, 0.4), inset 0 1px 0 rgba(255, 255, 255, 0.2);
  backdrop-filter: blur(12px);
  transition: all 0.25s ease;
}

.floating-console-trigger:hover {
  background: linear-gradient(135deg, rgba(12, 32, 60, 0.96) 0%, rgba(6, 18, 38, 0.98) 100%);
  box-shadow: 0 0 36px rgba(0, 229, 255, 0.65);
  transform: translateX(-50%) scale(1.04);
}

.trigger-badge {
  padding: 2px 6px;
  border-radius: 3px;
  background: rgba(62, 224, 200, 0.2);
  color: #3ee0c8;
  font-size: 10px;
  font-family: monospace;
}

.login-card__body {
  min-height: 240px;
}

/* ── Deep overrides for child components ─────────────────────────────────── */
:deep(.hint-muted),
:deep(.account-divider) {
  color: var(--login-muted) !important;
}

:deep(.unused-login__title) {
  color: var(--login-title);
}

:deep(.unused-login__hint) {
  color: var(--login-muted);
}

:deep(.n-button--primary-type) {
  background: linear-gradient(90deg, #00c8e6 0%, #00f0ff 100%) !important;
  color: #020d18 !important;
  font-weight: 700 !important;
  font-size: 14px !important;
  letter-spacing: 0.12em !important;
  border: none !important;
  box-shadow: 0 0 20px rgba(0, 229, 255, 0.5) !important;
  position: relative !important;
  overflow: hidden !important;
}

:deep(.n-button--primary-type::after) {
  content: '';
  position: absolute;
  top: 0;
  left: -100%;
  width: 60%;
  height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.4), transparent);
  transform: skewX(-20deg);
  animation: btnSheen 3.5s infinite;
}

@keyframes btnSheen {
  0%, 60% { left: -100%; }
  100% { left: 200%; }
}

:deep(.account-row .n-button) {
  background: rgba(0, 229, 255, 0.08) !important;
  border: 1px solid rgba(0, 229, 255, 0.28) !important;
  color: #c5e6fa !important;
  font-weight: 500 !important;
  transition: all 0.2s ease !important;
}

:deep(.account-row .n-button:hover) {
  background: rgba(0, 229, 255, 0.22) !important;
  border-color: #00e5ff !important;
  color: #ffffff !important;
  box-shadow: 0 0 12px rgba(0, 229, 255, 0.45) !important;
}

/* ── Transition ───────────────────────────────────────────────────────────── */
.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: all 0.3s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.fade-slide-enter-from,
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-50%) translateX(30px);
}

/* ── Responsive ───────────────────────────────────────────────────────────── */
@media (max-width: 1540px) {
  .login-stage {
    right: 280px;
  }
}

@media (max-width: 1440px) {
  .clock-date {
    display: none;
  }
}

@media (max-width: 1366px) {
  .hud-left,
  .hud-right {
    width: 250px;
    top: 80px;
  }

  .login-stage {
    right: 250px;
  }

  .brand {
    gap: 10px;
  }

  .brand__logo-img {
    max-width: 145px;
    height: 22px;
  }

  .brand__text h1 {
    font-size: 13.5px;
  }

  .brand__text p {
    font-size: 8.5px;
  }

  .telemetry-item {
    padding: 2px 7px;
    gap: 4px;
  }

  .telemetry-item__label {
    font-size: 10px;
  }

  .telemetry-item__val {
    font-size: 12.5px;
  }

  .task-table__header,
  .task-table__row {
    grid-template-columns: 78px 46px 1fr 48px;
  }
}

@media (max-width: 1200px) {
  .login-stage {
    left: 50%;
    right: auto;
    transform: translate(-50%, -50%);
  }

  .hud-left,
  .hud-right {
    opacity: 0.35;
    pointer-events: none;
  }

  .env-badge__zone {
    display: none;
  }
}

@media (max-width: 900px) {
  .kpi-row,
  .hud-bottom,
  .hud-left,
  .hud-right {
    display: none;
  }

  .hud-top {
    justify-content: space-between;
  }

  .login-stage {
    left: 50%;
    right: auto;
    top: 50%;
    transform: translate(-50%, -50%);
    width: calc(100vw - 32px);
  }

  .login-card {
    width: 100%;
  }
}

/* ── Light Mode Specific Polish Overrides ────────────────────────────────── */
.is-light .telemetry-ribbon {
  background: rgba(255, 255, 255, 0.92);
  border-color: rgba(0, 90, 255, 0.22);
  box-shadow: 0 4px 16px rgba(0, 50, 120, 0.08);
}

.is-light .telemetry-item {
  border-right-color: rgba(0, 90, 255, 0.12);
}

.is-light .telemetry-item__label {
  color: #64748b;
}

.is-light .zone-tag {
  background: rgba(0, 90, 255, 0.08);
  color: #005aff;
  border-color: rgba(0, 90, 255, 0.25);
}

.is-light .zone-metric {
  color: #64748b;
}

.is-light .alarm-badge--warn {
  background: rgba(245, 158, 11, 0.12);
  color: #d97706;
  border-color: rgba(245, 158, 11, 0.3);
}

.is-light .alarm-badge--info {
  background: rgba(0, 90, 255, 0.08);
  color: #005aff;
  border-color: rgba(0, 90, 255, 0.25);
}

.is-light .alarm-vehicle {
  color: #0a192f;
}

.is-light .alarm-time {
  color: #64748b;
}

.is-light .alarm-msg {
  color: #334155;
}

.is-light .hud-meta__clock {
  color: #005aff;
}

.is-light .env-badge {
  background: rgba(255, 255, 255, 0.88);
  border-color: rgba(0, 90, 255, 0.2);
  color: #1e293b;
}

.is-light .view-toggle-btn {
  background: rgba(255, 255, 255, 0.9);
  border-color: rgba(0, 90, 255, 0.3);
  color: #005aff;
}

.is-light .view-toggle-btn:hover {
  background: #005aff;
  color: #ffffff;
}

.is-light .panel-head h2 {
  color: #0a192f;
}

.is-light .panel-head__title .section-icon {
  color: #005aff;
}

.is-light .panel-more {
  color: #005aff;
}

.is-light .panel-sub-label {
  color: #64748b;
}

.is-light .task-table__header {
  background: rgba(0, 90, 255, 0.06);
  color: #64748b;
  border-bottom: 1px solid rgba(0, 90, 255, 0.12);
}

.is-light .task-table__row {
  color: #1e293b;
  border-bottom: 1px solid rgba(0, 90, 255, 0.06);
}

.is-light .task-table__row:hover {
  background: rgba(0, 90, 255, 0.05);
}

.is-light .task-id {
  color: #005aff;
}

.is-light .task-type {
  color: #334155;
}

.is-light .task-route {
  color: #475569;
}

.is-light .task-status--running {
  color: #005aff;
  background: rgba(0, 90, 255, 0.08);
  border-color: rgba(0, 90, 255, 0.3);
}

.is-light .task-status--waiting {
  color: #64748b;
  background: rgba(100, 116, 139, 0.08);
  border-color: rgba(100, 116, 139, 0.25);
}

.is-light .task-status--done {
  color: #00875a;
  background: rgba(0, 184, 148, 0.1);
  border-color: rgba(0, 184, 148, 0.3);
}

.is-light .donut__hole {
  background: #ffffff;
  box-shadow: inset 0 0 10px rgba(0, 50, 120, 0.08);
}

.is-light .donut__hole strong {
  color: #0a192f;
}

.is-light .donut__hole small {
  color: #64748b;
}

.is-light .status-label {
  color: #334155;
}

.is-light .status-num {
  color: #0a192f;
}

.is-light .status-pct {
  color: #64748b;
}

.is-light .sparkline-y-axis {
  color: #64748b;
}

.is-light .sparkline__labels {
  color: #64748b;
}

.is-light .mini-map-blueprint {
  background: #ffffff;
  border-color: rgba(0, 90, 255, 0.2);
  box-shadow: inset 0 0 16px rgba(0, 90, 255, 0.05);
}

.is-light .mini-map-legend-vertical {
  background: rgba(255, 255, 255, 0.88);
  border-color: rgba(0, 90, 255, 0.18);
  color: #334155;
}

.is-light .legend-item span {
  color: #334155;
}

.is-light .zone-table__header {
  background: rgba(0, 90, 255, 0.06);
  color: #64748b;
  border-bottom: 1px solid rgba(0, 90, 255, 0.12);
}

.is-light .zone-table__row {
  border-bottom: 1px solid rgba(0, 90, 255, 0.06);
}

.is-light .zone-name {
  color: #1e293b;
}

.is-light .zone-disc {
  color: #005aff;
}

.is-light .alarm-row {
  background: rgba(255, 255, 255, 0.85);
  border-color: rgba(0, 90, 255, 0.14);
}

.is-light .alarm-vehicle {
  color: #005aff;
}

.is-light .alarm-msg {
  color: #334155;
}

.is-light .alarm-time {
  color: #64748b;
}

.is-light .feature__icon-wrap {
  background: rgba(0, 90, 255, 0.07);
  border-color: rgba(0, 90, 255, 0.22);
}

.is-light .feature__text strong {
  color: #0a192f;
}

.is-light .feature__text span {
  color: #64748b;
}

.is-light .login-card {
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.95) 0%, rgba(245, 250, 255, 0.96) 100%);
  border-color: rgba(0, 90, 255, 0.3);
  box-shadow: 0 16px 48px rgba(0, 50, 120, 0.16);
}

.is-light .login-card__kicker {
  color: #005aff;
}

.is-light .login-card__head h3 {
  color: #0a192f;
}

.is-light .login-card__badge {
  background: rgba(0, 184, 148, 0.1);
  border-color: rgba(0, 184, 148, 0.35);
  color: #00875a;
}

.is-light .card-minimize-btn {
  background: rgba(0, 90, 255, 0.08);
  border-color: rgba(0, 90, 255, 0.25);
  color: #005aff;
}

.is-light .card-minimize-btn:hover {
  background: #005aff;
  color: #ffffff;
}

.is-light .cyber-corner {
  border-color: #005aff;
}

.is-light .trigger-console-btn {
  background: rgba(255, 255, 255, 0.96);
  border-color: rgba(0, 90, 255, 0.35);
  color: #005aff;
  box-shadow: 0 8px 30px rgba(0, 60, 140, 0.16);
}

.is-light .trigger-icon {
  color: #005aff;
}

.is-light .trigger-text {
  color: #0a192f;
}

.is-light .trigger-badge {
  background: rgba(0, 184, 148, 0.15);
  border-color: rgba(0, 184, 148, 0.4);
  color: #00875a;
}

.is-light .spatial-zone__en {
  color: #005aff;
  font-weight: 800;
}

.is-light .spatial-zone__cn {
  color: #0a192f;
  font-weight: 600;
}

.is-light .hud-badge__pill {
  background: rgba(255, 255, 255, 0.95);
  border-color: rgba(0, 90, 255, 0.4);
  box-shadow: 0 4px 16px rgba(0, 50, 120, 0.15);
}

.is-light .hud-badge__name {
  color: #0a192f;
}

/* ── Login Form & Account Buttons Deep Styling ───────────────────────────── */
.login-card :deep(.n-divider__title) {
  color: #7dd3fc !important;
  font-size: 12px;
}

.login-card :deep(.hint-muted) {
  color: #7dd3fc !important;
}

.login-card :deep(.n-button--secondary) {
  background: rgba(0, 229, 255, 0.08) !important;
  border: 1px solid rgba(0, 229, 255, 0.3) !important;
  color: #d7efff !important;
  font-weight: 600;
  transition: all 0.2s ease;
}

.login-card :deep(.n-button--secondary:hover) {
  background: rgba(0, 229, 255, 0.22) !important;
  border-color: #00e5ff !important;
  color: #ffffff !important;
  box-shadow: 0 0 12px rgba(0, 229, 255, 0.4);
}

.is-light .login-card :deep(.n-divider__title) {
  color: #475569 !important;
  font-size: 12px;
  font-weight: 500;
}

.is-light .login-card :deep(.hint-muted) {
  color: #64748b !important;
}

.is-light .login-card :deep(.n-button--secondary) {
  background: rgba(0, 90, 255, 0.06) !important;
  border: 1px solid rgba(0, 90, 255, 0.28) !important;
  color: #0a192f !important;
  font-weight: 600;
  transition: all 0.2s ease;
}

.is-light .login-card :deep(.n-button--secondary:hover) {
  background: rgba(0, 90, 255, 0.14) !important;
  border-color: #005aff !important;
  color: #005aff !important;
  box-shadow: 0 2px 10px rgba(0, 90, 255, 0.2);
}
</style>
