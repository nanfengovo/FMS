<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { watchDebounced } from '@vueuse/core';
import type { DataTableColumns } from 'naive-ui';
import { NButton, NTag } from 'naive-ui';
import { useAuth } from '@/hooks/business/auth';
import { fmsApi } from '@/service/api/fms';
import {
  actorLabel,
  formatLogLine,
  formatTimestamp,
  logLevelLabel,
  logLevelTag,
  logOriginLabel,
  logOriginTag,
  normalizeLogEvent
} from '@/utils/fms-display';

const props = withDefaults(
  defineProps<{
    category?: string;
    source?: string;
    view?: 'text' | 'structured';
    title?: string;
  }>(),
  {
    view: 'structured',
    title: '日志中心'
  }
);

const router = useRouter();
const { hasAuth, currentRoleInfo } = useAuth();

const keyword = ref('');
const loading = ref(false);
const page = ref(1);
const pageSize = ref(50);
const items = ref<Fms.LogEventEntry[]>([]);
const lines = ref<string[]>([]);
const total = ref(0);
const selected = ref<Fms.LogEventEntry | null>(null);
const drawerOpen = ref(false);
const viewMode = ref<'text' | 'structured'>(props.view);
const sourceChip = ref(initialSource());
const level = ref('');
const timePreset = ref<'all' | '1h' | '24h' | '7d'>('all');
const taskSerial = ref('');
const vehicleId = ref('');
const related = ref<Fms.LogEventEntry[]>([]);
const relatedLoading = ref(false);
const relatedMode = ref<'task' | 'vehicle' | null>(null);

const pageSizes = [20, 50, 100];
const canAnalyze = computed(() => hasAuth('Fms.Logs.Analyze'));
const canSettings = computed(() => hasAuth('Fms.Logs.Settings'));
const analyzeReason = computed(() =>
  canAnalyze.value ? '用检索和 Wiki 归纳日志' : `当前角色【${currentRoleInfo.value.label}】没有 AI 分析权限`
);
const settingsReason = computed(() =>
  canSettings.value ? '配置日志分析接口密钥' : `当前角色【${currentRoleInfo.value.label}】没有配置接口密钥权限`
);

const sourceOptions = [
  { label: '全部', value: '' },
  { label: '入站', value: 'Inbound' },
  { label: '出站', value: 'Outbound' },
  { label: '接口', value: 'Api' },
  { label: '用户操作', value: 'UserOperation' },
  { label: '运行', value: 'Runtime' },
  { label: '审计', value: 'Audit' }
];

const levelOptions = [
  { label: '全部级别', value: '' },
  { label: '信息', value: 'Information' },
  { label: '警告', value: 'Warning' },
  { label: '错误', value: 'Error' },
  { label: '调试', value: 'Debug' }
];

const timeOptions = [
  { label: '全部时间', value: 'all' },
  { label: '近 1 小时', value: '1h' },
  { label: '近 24 小时', value: '24h' },
  { label: '近 7 天', value: '7d' }
] as const;

const correlationText = computed(() => {
  if (taskSerial.value) return `任务流水号 ${taskSerial.value}`;
  if (vehicleId.value) return `车辆 ${vehicleId.value}`;
  return '';
});

const columns = computed<DataTableColumns<Fms.LogEventEntry>>(() => buildColumns((page.value - 1) * pageSize.value));
const relatedColumns = computed<DataTableColumns<Fms.LogEventEntry>>(() => buildColumns(0));

function initialSource() {
  if (props.source) return props.source;
  return props.category || '';
}

function timeBounds(): { from?: string; to?: string } {
  if (timePreset.value === 'all') return {};
  const to = new Date();
  const from = new Date(to.getTime());
  if (timePreset.value === '1h') from.setHours(from.getHours() - 1);
  if (timePreset.value === '24h') from.setTime(from.getTime() - 24 * 60 * 60 * 1000);
  if (timePreset.value === '7d') from.setTime(from.getTime() - 7 * 24 * 60 * 60 * 1000);
  return { from: from.toISOString(), to: to.toISOString() };
}

function buildQuery(overrides: Fms.LogQueryParams = {}): Fms.LogQueryParams {
  const chip = sourceChip.value;
  const bounds = timeBounds();
  const serial = taskSerial.value || undefined;
  const vehicle = vehicleId.value || undefined;
  const typedKeyword = keyword.value.trim() || undefined;
  return {
    category: chip === 'Audit' ? 'UserOperation' : chip || undefined,
    source: chip === 'Audit' ? 'Audit' : undefined,
    keyword: typedKeyword || serial || vehicle,
    level: level.value || undefined,
    from: bounds.from,
    to: bounds.to,
    taskSerial: serial,
    vehicleId: vehicle,
    page: page.value,
    pageSize: pageSize.value,
    ...overrides
  };
}

function originOf(row: Fms.LogEventEntry) {
  return logOriginLabel(row.category, row.source);
}

function buildColumns(seqBase: number): DataTableColumns<Fms.LogEventEntry> {
  return [
    {
      title: '序号',
      key: 'seq',
      width: 72,
      align: 'center',
      render: (_row, index) => String(seqBase + index + 1)
    },
    {
      title: '时间',
      key: 'timestamp',
      width: 196,
      render: row => formatTimestamp(row.timestamp)
    },
    {
      title: '级别',
      key: 'level',
      width: 88,
      render: row =>
        h(NTag, { size: 'small', bordered: false, type: logLevelTag(row.level) }, { default: () => logLevelLabel(row.level) })
    },
    {
      title: '来源',
      key: 'source',
      width: 108,
      render: row =>
        h(
          NTag,
          { size: 'small', bordered: false, type: logOriginTag(row.category, row.source) },
          { default: () => originOf(row) }
        )
    },
    {
      title: '操作者',
      key: 'actor',
      width: 120,
      ellipsis: { tooltip: true },
      render: row => actorLabel(row.actor) || row.actor || '—'
    },
    {
      title: '任务 / 车辆',
      key: 'correlation',
      width: 168,
      render: row => {
        const nodes = [];
        if (row.taskSerial) {
          nodes.push(
            h(
              NButton,
              {
                size: 'tiny',
                quaternary: true,
                type: 'primary',
                onClick: (event: MouseEvent) => {
                  event.stopPropagation();
                  narrowByTask(row.taskSerial!);
                }
              },
              { default: () => row.taskSerial }
            )
          );
        }
        if (row.vehicleId) {
          nodes.push(
            h(
              NButton,
              {
                size: 'tiny',
                quaternary: true,
                type: 'info',
                onClick: (event: MouseEvent) => {
                  event.stopPropagation();
                  narrowByVehicle(row.vehicleId!);
                }
              },
              { default: () => row.vehicleId }
            )
          );
        }
        if (nodes.length === 0) return '—';
        return h('div', { class: 'flex flex-col items-start gap-0.5' }, nodes);
      }
    },
    {
      title: '消息',
      key: 'message',
      ellipsis: { tooltip: true },
      render: row => {
        const path = row.path ? `${row.method || ''} ${row.path}`.trim() : '';
        return path && !row.message.includes(path) ? `${row.message}  ·  ${path}` : row.message;
      }
    },
    {
      title: '',
      key: 'related',
      width: 92,
      render: row =>
        h(
          NButton,
          {
            size: 'tiny',
            tertiary: true,
            disabled: !row.taskSerial && !row.vehicleId,
            onClick: (event: MouseEvent) => {
              event.stopPropagation();
              openDetail(row, true);
            }
          },
          { default: () => '关联记录' }
        )
    }
  ];
}

async function load() {
  loading.value = true;
  try {
    const result = await fmsApi.queryLogs(buildQuery());
    items.value = (result.items || []).map(normalizeLogEvent);
    lines.value = result.lines || [];
    total.value = result.total || 0;
  } catch {
    items.value = [];
    lines.value = [];
    total.value = 0;
    window.$message?.error('日志查询失败');
  } finally {
    loading.value = false;
  }
}

function resetToFirstPageAndLoad() {
  page.value = 1;
  load();
}

async function loadRelated(row: Fms.LogEventEntry, mode: 'task' | 'vehicle') {
  const serial = mode === 'task' ? row.taskSerial || undefined : undefined;
  const vehicle = mode === 'vehicle' ? row.vehicleId || undefined : undefined;
  if (!serial && !vehicle) {
    related.value = [];
    relatedMode.value = null;
    return;
  }
  relatedMode.value = mode;
  relatedLoading.value = true;
  try {
    const result = await fmsApi.queryLogs({
      keyword: serial || vehicle,
      taskSerial: serial,
      vehicleId: vehicle,
      page: 1,
      pageSize: 100
    });
    related.value = (result.items || []).map(normalizeLogEvent);
  } catch {
    related.value = [];
    window.$message?.error('关联记录查询失败');
  } finally {
    relatedLoading.value = false;
  }
}

function openDetail(row: Fms.LogEventEntry, loadLinks = false) {
  selected.value = row;
  drawerOpen.value = true;
  if (loadLinks) {
    const mode = row.taskSerial ? 'task' : row.vehicleId ? 'vehicle' : null;
    if (mode) loadRelated(row, mode);
    else {
      related.value = [];
      relatedMode.value = null;
    }
  }
}

function narrowByTask(serial: string) {
  keyword.value = '';
  vehicleId.value = '';
  sourceChip.value = '';
  drawerOpen.value = false;
  taskSerial.value = serial;
}

function narrowByVehicle(id: string) {
  keyword.value = '';
  taskSerial.value = '';
  sourceChip.value = '';
  drawerOpen.value = false;
  vehicleId.value = id;
}

function clearCorrelation() {
  taskSerial.value = '';
  vehicleId.value = '';
}

function goAnalyze() {
  if (!canAnalyze.value) return;
  router.push('/logs/ai');
}

function goSettings() {
  if (!canSettings.value) return;
  router.push('/logs/openapi');
}

function onPageSizeChange(size: number) {
  pageSize.value = size;
  resetToFirstPageAndLoad();
}

function relatedKeyHint() {
  if (relatedMode.value === 'task') return selected.value?.taskSerial || '';
  if (relatedMode.value === 'vehicle') return selected.value?.vehicleId || '';
  return '';
}

watch([() => props.category, () => props.source, () => props.view], () => {
  sourceChip.value = initialSource();
  viewMode.value = props.view;
});

watch([sourceChip, level, timePreset, taskSerial, vehicleId], resetToFirstPageAndLoad);
watchDebounced(keyword, resetToFirstPageAndLoad, { debounce: 320 });
onMounted(load);
</script>

<template>
  <div class="log-center w-full p-4">
    <NCard :bordered="false" class="log-center__card shadow-sm">
      <template #header>
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div class="text-16px font-semibold">日志中心</div>
            <div class="mt-0.5 text-12px text-slate-500 dark:text-slate-400">入站、出站、接口、用户操作、运行与审计共用同一时间线</div>
          </div>
          <NSpace align="center" :wrap="true" :size="8">
            <NInput
              v-model:value="keyword"
              clearable
              size="small"
              placeholder="搜索消息、操作者、接口路径、任务流水号、车辆编号"
              style="width: 360px"
            >
              <template #prefix><div class="i-carbon-search text-slate-400" /></template>
            </NInput>
            <NButton size="small" :loading="loading" @click="load">刷新</NButton>
            <NTooltip>
              <template #trigger>
                <span class="inline-flex">
                  <NButton size="small" :disabled="!canAnalyze" :title="analyzeReason" @click="goAnalyze">AI 分析</NButton>
                </span>
              </template>
              {{ analyzeReason }}
            </NTooltip>
            <NTooltip>
              <template #trigger>
                <span class="inline-flex">
                  <NButton size="small" :disabled="!canSettings" :title="settingsReason" @click="goSettings">接口密钥</NButton>
                </span>
              </template>
              {{ settingsReason }}
            </NTooltip>
            <NRadioGroup v-model:value="viewMode" size="small">
              <NRadioButton value="structured">表格</NRadioButton>
              <NRadioButton value="text">文本</NRadioButton>
            </NRadioGroup>
          </NSpace>
        </div>
      </template>

      <div class="log-center__toolbar mb-3 space-y-2">
        <div class="flex flex-wrap items-center gap-1.5">
          <span class="mr-1 text-12px text-slate-500">来源</span>
          <NButton
            v-for="option in sourceOptions"
            :key="option.value || 'all'"
            size="tiny"
            round
            :type="sourceChip === option.value ? 'primary' : 'default'"
            :ghost="sourceChip !== option.value"
            @click="sourceChip = option.value"
          >
            {{ option.label }}
          </NButton>
        </div>
        <div class="flex flex-wrap items-center gap-3">
          <div class="flex flex-wrap items-center gap-1.5">
            <span class="mr-1 text-12px text-slate-500">级别</span>
            <NButton
              v-for="option in levelOptions"
              :key="option.value || 'all-level'"
              size="tiny"
              round
              :type="level === option.value ? 'primary' : 'default'"
              :ghost="level !== option.value"
              @click="level = option.value"
            >
              {{ option.label }}
            </NButton>
          </div>
          <div class="flex flex-wrap items-center gap-1.5">
            <span class="mr-1 text-12px text-slate-500">时间</span>
            <NButton
              v-for="option in timeOptions"
              :key="option.value"
              size="tiny"
              round
              :type="timePreset === option.value ? 'primary' : 'default'"
              :ghost="timePreset !== option.value"
              @click="timePreset = option.value"
            >
              {{ option.label }}
            </NButton>
          </div>
        </div>
        <div v-if="correlationText" class="flex items-center gap-2 rounded border border-cyan-500/30 bg-cyan-500/8 px-3 py-1.5 text-12px text-cyan-700 dark:text-cyan-200">
          <span>已按 {{ correlationText }} 收窄，来源标签仍保留在同一时间线</span>
          <NButton size="tiny" quaternary @click="clearCorrelation">清除</NButton>
        </div>
      </div>

      <pre
        v-if="viewMode === 'text'"
        class="log-center__text max-h-[calc(100vh-320px)] min-h-80 overflow-auto whitespace-pre-wrap break-all rounded bg-slate-950 p-3 text-xs leading-6 text-slate-100"
      >{{ lines.map(line => formatLogLine(line)).join('\n') || '暂无符合条件的日志' }}</pre>
      <NDataTable
        v-else
        class="log-center__table"
        size="small"
        :columns="columns"
        :data="items"
        :loading="loading"
        :bordered="true"
        :single-line="true"
        :row-key="(row: Fms.LogEventEntry) => row.id"
        :scroll-x="1180"
        :max-height="'calc(100vh - 340px)'"
        :row-props="(row: Fms.LogEventEntry) => ({ style: 'cursor: pointer; height: 44px;', onClick: () => openDetail(row, true) })"
      >
        <template #empty>
          <NEmpty description="暂无符合条件的日志" size="small" class="py-10" />
        </template>
      </NDataTable>

      <div class="mt-3 flex flex-wrap items-center justify-between gap-3">
        <div class="text-12px text-slate-500">
          共 <strong class="text-slate-800 dark:text-slate-200">{{ total }}</strong> 条
          <span v-if="total > 0" class="ml-1">
            · 第 {{ page }} / {{ Math.max(1, Math.ceil(total / pageSize)) }} 页
            · 本页序号 {{ total === 0 ? 0 : (page - 1) * pageSize + 1 }}–{{ Math.min(page * pageSize, total) }}
          </span>
        </div>
        <NPagination
          v-model:page="page"
          :page-size="pageSize"
          :item-count="total"
          :page-sizes="pageSizes"
          show-size-picker
          show-quick-jumper
          size="small"
          @update:page="load"
          @update:page-size="onPageSizeChange"
        />
      </div>
    </NCard>

    <NDrawer v-model:show="drawerOpen" :width="720" display-directive="show">
      <NDrawerContent :native-scrollbar="false" closable title="日志详情与关联记录">
        <div v-if="selected" class="space-y-4">
          <div class="grid grid-cols-2 gap-2 text-12px">
            <div>时间：{{ formatTimestamp(selected.timestamp) }}</div>
            <div>级别：{{ logLevelLabel(selected.level) }}</div>
            <div>来源：{{ originOf(selected) }}</div>
            <div>操作者：{{ actorLabel(selected.actor) || selected.actor || '—' }}</div>
            <div>任务流水号：{{ selected.taskSerial || '—' }}</div>
            <div>车辆编号：{{ selected.vehicleId || '—' }}</div>
            <div class="col-span-2">路径：{{ selected.path || '—' }}</div>
            <div class="col-span-2">消息：{{ selected.message }}</div>
          </div>
          <NSpace>
            <NButton
              size="small"
              :disabled="!selected.taskSerial"
              :title="selected.taskSerial ? '按任务流水号收窄主表' : '这条日志没有任务流水号'"
              @click="narrowByTask(selected.taskSerial!)"
            >
              按任务收窄
            </NButton>
            <NButton
              size="small"
              :disabled="!selected.vehicleId"
              :title="selected.vehicleId ? '按车辆编号收窄主表' : '这条日志没有车辆编号'"
              @click="narrowByVehicle(selected.vehicleId!)"
            >
              按车辆收窄
            </NButton>
            <NButton
              v-if="selected.taskSerial"
              size="small"
              tertiary
              :loading="relatedLoading && relatedMode === 'task'"
              @click="loadRelated(selected, 'task')"
            >
              任务关联时间线
            </NButton>
            <NButton
              v-if="selected.vehicleId"
              size="small"
              tertiary
              :loading="relatedLoading && relatedMode === 'vehicle'"
              @click="loadRelated(selected, 'vehicle')"
            >
              车辆关联时间线
            </NButton>
          </NSpace>
          <div>
            <div class="mb-2 text-13px font-medium">
              关联记录
              <span v-if="relatedMode" class="ml-1 text-12px font-normal text-slate-500">
                · {{ relatedMode === 'task' ? '任务' : '车辆' }} {{ relatedKeyHint() }} · {{ related.length }} 条 · 时间倒序
              </span>
            </div>
            <NDataTable
              class="log-center__table"
              size="small"
              :columns="relatedColumns"
              :data="related"
              :loading="relatedLoading"
              :bordered="true"
              :row-key="(row: Fms.LogEventEntry) => row.id"
              :max-height="360"
              :row-props="(row: Fms.LogEventEntry) => ({ style: 'cursor: pointer; height: 44px;', onClick: () => openDetail(row, false) })"
            >
              <template #empty>
                <NEmpty :description="selected.taskSerial || selected.vehicleId ? '没有找到同一流水号或车辆的其他记录' : '这条日志没有可串联的任务流水号或车辆编号'" size="small" class="py-6" />
              </template>
            </NDataTable>
          </div>
        </div>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.log-center__card {
  border: 1px solid rgb(148 163 184 / 18%);
}
.log-center__table :deep(.n-data-table-th) {
  height: 40px;
}
.log-center__table :deep(.n-data-table-td) {
  height: 44px;
}
</style>
