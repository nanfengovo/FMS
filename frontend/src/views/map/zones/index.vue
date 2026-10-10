<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { useMapStore } from '@/store/modules/map';
import { useSimulationStore } from '@/store/modules/simulation';
import { fmsApi } from '@/service/api/fms';

const { checkPermission, currentRoleInfo } = useAuth();
const mapStore = useMapStore();
const simStore = useSimulationStore();

const activeTab = ref<'bins' | 'zones'>('bins');

// Storage Bins Management
export interface StorageBin {
  id: string;
  zoneType: 'RAW' | 'WIP' | 'FINISHED' | 'CHARGE' | 'PARK';
  name: string;
  status: 'Empty' | 'Occupied' | 'Reserved' | 'Locked';
  palletCode?: string;
  updatedAt: string;
}

const storageBins = ref<StorageBin[]>([
  // 原料区
  { id: 'A-01', zoneType: 'RAW', name: '演示货位 A-01', status: 'Occupied', palletCode: 'FOUP-8801', updatedAt: '10:12:00' },
  { id: 'A-02', zoneType: 'RAW', name: '演示货位 A-02', status: 'Occupied', palletCode: 'FOUP-8802', updatedAt: '10:15:30' },
  { id: 'A-03', zoneType: 'RAW', name: '演示货位 A-03', status: 'Reserved', palletCode: 'FOUP-8803', updatedAt: '10:20:00' },
  { id: 'A-04', zoneType: 'RAW', name: '演示货位 A-04', status: 'Empty', palletCode: '', updatedAt: '09:40:00' },
  { id: 'A-05', zoneType: 'RAW', name: '演示货位 A-05', status: 'Empty', palletCode: '', updatedAt: '09:40:00' },
  { id: 'A-06', zoneType: 'RAW', name: '演示货位 A-06', status: 'Locked', palletCode: 'ERR-SENSOR', updatedAt: '08:00:00' },

  // 线边缓存区
  { id: 'B-01', zoneType: 'WIP', name: '演示货位 B-01', status: 'Occupied', palletCode: 'WIP-TRAY-101', updatedAt: '10:22:15' },
  { id: 'B-02', zoneType: 'WIP', name: '演示货位 B-02', status: 'Empty', palletCode: '', updatedAt: '09:30:00' },
  { id: 'B-03', zoneType: 'WIP', name: '演示货位 B-03', status: 'Reserved', palletCode: 'WIP-TRAY-103', updatedAt: '10:24:00' },
  { id: 'B-04', zoneType: 'WIP', name: '演示货位 B-04', status: 'Empty', palletCode: '', updatedAt: '09:30:00' },
  { id: 'B-05', zoneType: 'WIP', name: '演示货位 B-05', status: 'Occupied', palletCode: 'WIP-TRAY-105', updatedAt: '10:25:40' },
  { id: 'B-06', zoneType: 'WIP', name: '演示货位 B-06', status: 'Empty', palletCode: '', updatedAt: '09:30:00' },

  // 成品区
  { id: 'C-01', zoneType: 'FINISHED', name: '演示货位 C-01', status: 'Empty', palletCode: '', updatedAt: '09:00:00' },
  { id: 'C-02', zoneType: 'FINISHED', name: '演示货位 C-02', status: 'Occupied', palletCode: 'FIN-BOX-501', updatedAt: '10:10:00' },
  { id: 'C-03', zoneType: 'FINISHED', name: '演示货位 C-03', status: 'Occupied', palletCode: 'FIN-BOX-502', updatedAt: '10:18:22' },
  { id: 'C-04', zoneType: 'FINISHED', name: '演示货位 C-04', status: 'Empty', palletCode: '', updatedAt: '09:00:00' },

  // 充电区
  { id: 'CH-01', zoneType: 'CHARGE', name: '演示充电货位 CH-01', status: 'Empty', palletCode: 'CHARGER-READY', updatedAt: '10:00:00' },
  { id: 'CH-02', zoneType: 'CHARGE', name: '演示充电货位 CH-02', status: 'Empty', palletCode: 'CHARGER-READY', updatedAt: '10:00:00' },

  // 专属待命区
  { id: 'P-01', zoneType: 'PARK', name: '演示待命货位 P-01', status: 'Empty', palletCode: 'STANDBY-BAY', updatedAt: '09:00:00' },
  { id: 'P-02', zoneType: 'PARK', name: '演示待命货位 P-02', status: 'Occupied', palletCode: 'AMR-102 停靠', updatedAt: '10:20:00' }
]);

const filterZoneType = ref<string>('ALL');
const filterBinStatus = ref<string>('ALL');
const binSearch = ref<string>('');
const binPage = ref(1);
const binPageSize = ref(12);

watch([filterZoneType, filterBinStatus, binSearch], () => {
  binPage.value = 1;
});

const filteredBins = computed(() => {
  return storageBins.value.filter(b => {
    const matchZone = filterZoneType.value === 'ALL' || b.zoneType === filterZoneType.value;
    const matchStatus = filterBinStatus.value === 'ALL' || b.status === filterBinStatus.value;
    const matchSearch =
      !binSearch.value ||
      b.id.toLowerCase().includes(binSearch.value.toLowerCase()) ||
      b.name.toLowerCase().includes(binSearch.value.toLowerCase()) ||
      (b.palletCode && b.palletCode.toLowerCase().includes(binSearch.value.toLowerCase()));
    return matchZone && matchStatus && matchSearch;
  });
});

const paginatedBins = computed(() => {
  const start = (binPage.value - 1) * binPageSize.value;
  return filteredBins.value.slice(start, start + binPageSize.value);
});

const binStats = computed(() => {
  const total = storageBins.value.length;
  const empty = storageBins.value.filter(b => b.status === 'Empty').length;
  const occupied = storageBins.value.filter(b => b.status === 'Occupied').length;
  const reserved = storageBins.value.filter(b => b.status === 'Reserved').length;
  const locked = storageBins.value.filter(b => b.status === 'Locked').length;
  return { total, empty, occupied, reserved, locked };
});

async function loadBackendStorageData() {
  try {
    const slots = await fmsApi.getStorageSlots();
    if (slots && slots.length > 0) {
      storageBins.value = slots.map((s: any) => ({
        id: s.id,
        zoneType: (s.zoneType || 'WIP').toUpperCase() as any,
        name: s.name || s.id,
        status: s.status as any,
        palletCode: s.occupiedCarrierId || (s.status === 'Locked' ? (s.lockReason || '检修锁定') : ''),
        updatedAt: s.updatedAt ? new Date(s.updatedAt).toLocaleTimeString() : new Date().toLocaleTimeString()
      }));
    }
  } catch (err) {
    console.error('Failed to load storage slots from backend:', err);
  }
}

async function toggleBinStatus(bin: StorageBin) {
  const states: StorageBin['status'][] = ['Empty', 'Occupied', 'Reserved', 'Locked'];
  const nextIdx = (states.indexOf(bin.status) + 1) % states.length;
  const targetStatus = states[nextIdx];
  const locking = targetStatus === 'Locked';
  if (!checkPermission(locking ? 'Fms.Storage.Lock' : 'Fms.Storage.Allocate', locking ? '锁定货位' : '分配库位状态')) return;

  try {
    await fmsApi.updateSlotStatus(
      bin.id,
      targetStatus,
      targetStatus === 'Occupied' ? (bin.palletCode || 'FOUP-' + bin.id) : undefined,
      targetStatus === 'Locked' ? '现场人工锁定检修' : undefined
    );
    bin.status = targetStatus;
    if (targetStatus === 'Empty') bin.palletCode = '';
    if (targetStatus === 'Occupied' && !bin.palletCode) bin.palletCode = 'FOUP-' + bin.id;
    if (targetStatus === 'Locked') bin.palletCode = '检修锁定';
    bin.updatedAt = new Date().toLocaleTimeString();
    window.$message?.success(`库位【${bin.id}】状态已同步为: ${getBinStatusText(bin.status)}`);
  } catch (err: any) {
    window.$message?.error(`更新失败: ${err.message || '网络异常'}`);
  }
}

function getBinStatusText(status: string) {
  switch (status) {
    case 'Empty': return '空闲';
    case 'Occupied': return '占用';
    case 'Reserved': return '预约';
    case 'Locked': return '锁定';
    default: return status;
  }
}

function getBinStatusColor(status: string) {
  switch (status) {
    case 'Empty': return 'bg-emerald-500/10 border-emerald-500/40 text-emerald-600 dark:text-emerald-400';
    case 'Occupied': return 'bg-blue-500/10 border-blue-500/40 text-blue-600 dark:text-blue-400';
    case 'Reserved': return 'bg-amber-500/10 border-amber-500/40 text-amber-600 dark:text-amber-400';
    case 'Locked': return 'bg-gray-500/10 border-gray-500/40 text-gray-500';
    default: return 'bg-slate-500/10 border-slate-500/40 text-slate-500';
  }
}

const zones = computed(() => {
  const g = mapStore.currentGraph as any;
  if (g?.zones?.length) return g.zones;
  if (g?.mutexZones?.length) {
    return g.mutexZones.map((m: any) => ({
      id: m.zoneId || m.id,
      name: m.name || m.zoneId,
      maxCapacity: m.maxAllowedVehicles || 1,
      timeoutSec: 30,
      occupied: (m.currentOccupantVehicleIds?.length || 0) > 0
    }));
  }
  return [];
});

const zoneSearch = ref('');
const zonePage = ref(1);
const zonePageSize = ref(12);

watch(zoneSearch, () => {
  zonePage.value = 1;
});

const filteredZones = computed(() => {
  return zones.value.filter((z: any) => {
    return (
      !zoneSearch.value ||
      z.id.toLowerCase().includes(zoneSearch.value.toLowerCase()) ||
      z.name.toLowerCase().includes(zoneSearch.value.toLowerCase())
    );
  });
});

const paginatedZones = computed(() => {
  const start = (zonePage.value - 1) * zonePageSize.value;
  return filteredZones.value.slice(start, start + zonePageSize.value);
});

const simTestX = ref(0);
const simTestY = ref(0);
const simResult = ref<{ tested: boolean; inside: boolean; intersectionCount: number; names: string[] } | null>(null);

function pointInPolygon(x: number, y: number, polygon: Array<{ x: number; y: number }>) {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i, i += 1) {
    const xi = polygon[i].x;
    const yi = polygon[i].y;
    const xj = polygon[j].x;
    const yj = polygon[j].y;
    const crosses = (yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi || 1e-9) + xi;
    if (crosses) inside = !inside;
  }
  return inside;
}

function runRayCastingTest() {
  const px = Number(simTestX.value);
  const py = Number(simTestY.value);
  const zones = (mapStore.currentGraph?.mutexZones || []).filter(zone => zone.polygon && zone.polygon.length >= 3);
  const hits = zones.filter(zone => zone.polygon && pointInPolygon(px, py, zone.polygon));
  simResult.value = {
    tested: true,
    inside: hits.length > 0,
    intersectionCount: hits.length,
    names: hits.map(zone => zone.name || zone.zoneId)
  };
}

onMounted(async () => {
  await Promise.all([
    mapStore.loadCurrentMap(),
    loadBackendStorageData()
  ]);
  runRayCastingTest();
});
</script>

<template>
  <div class="w-full space-y-4 p-4 pb-8">
    <!-- Header -->
    <NCard title="库区与互斥" size="medium" class="shadow-sm">
      <template #header-extra>
        <NSpace align="center" :size="8">
          <NTag size="small" round :bordered="false" type="warning">演示账本 · 绑定当前路网待命点/充电点</NTag>
          <NButtonGroup size="small">
            <NButton :type="activeTab === 'bins' ? 'primary' : 'default'" @click="activeTab = 'bins'">
              <template #icon><div class="i-carbon-grid" /></template>
              储位网格管控
            </NButton>
            <NButton :type="activeTab === 'zones' ? 'primary' : 'default'" @click="activeTab = 'zones'">
              <template #icon><div class="i-carbon-area" /></template>
              几何互斥防冲区
            </NButton>
          </NButtonGroup>
        </NSpace>
      </template>

      <!-- TAB 1: Storage Bin Matrix -->
      <div v-if="activeTab === 'bins'" class="space-y-4 pt-1">
        <!-- KPI Strip -->
        <div class="grid grid-cols-2 gap-4 md:grid-cols-5">
          <NCard size="small" class="shadow-sm">
            <NStatistic label="演示货位数" :value="binStats.total" />
          </NCard>
          <NCard size="small" class="shadow-sm">
            <NStatistic label="当前空闲" :value="binStats.empty">
              <template #prefix><div class="i-carbon-checkmark text-emerald-500" /></template>
            </NStatistic>
          </NCard>
          <NCard size="small" class="shadow-sm">
            <NStatistic label="实物占用" :value="binStats.occupied">
              <template #prefix><div class="i-carbon-box text-blue-500" /></template>
            </NStatistic>
          </NCard>
          <NCard size="small" class="shadow-sm">
            <NStatistic label="任务预约" :value="binStats.reserved">
              <template #prefix><div class="i-carbon-hourglass text-amber-500" /></template>
            </NStatistic>
          </NCard>
          <NCard size="small" class="shadow-sm">
            <NStatistic label="维护锁定" :value="binStats.locked">
              <template #prefix><div class="i-carbon-locked text-gray-400" /></template>
            </NStatistic>
          </NCard>
        </div>

        <!-- Filter Bar -->
        <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-100 dark:border-slate-800">
          <NSpace align="center" :size="12">
            <NInput
              v-model:value="binSearch"
              placeholder="搜索储位 ID / 名称 / 载具编码..."
              clearable
              size="small"
              :style="{ width: '240px' }"
            >
              <template #prefix><div class="i-carbon-search text-gray-400" /></template>
            </NInput>

            <NSelect
              v-model:value="filterZoneType"
              size="small"
              :style="{ width: '170px' }"
              :options="[
                { label: '全部库区分类', value: 'ALL' },
                { label: '原料暂存区 (A)', value: 'RAW' },
                { label: '线边缓存区 (B)', value: 'WIP' },
                { label: '成品仓位区 (C)', value: 'FINISHED' },
                { label: '充电桩专用区', value: 'CHARGE' },
                { label: '专属待命车位', value: 'PARK' }
              ]"
            />

            <NSelect
              v-model:value="filterBinStatus"
              size="small"
              :style="{ width: '140px' }"
              :options="[
                { label: '全部储位状态', value: 'ALL' },
                { label: '空闲', value: 'Empty' },
                { label: '占用', value: 'Occupied' },
                { label: '预约', value: 'Reserved' },
                { label: '锁定', value: 'Locked' }
              ]"
            />
          </NSpace>

          <div class="flex items-center gap-3">
            <div class="hidden lg:flex items-center gap-2 text-[11px] text-gray-500">
              <span class="flex items-center gap-1"><span class="w-2 h-2 rounded-full bg-emerald-500 inline-block" /> 空闲</span>
              <span class="flex items-center gap-1"><span class="w-2 h-2 rounded-full bg-blue-500 inline-block" /> 占用</span>
              <span class="flex items-center gap-1"><span class="w-2 h-2 rounded-full bg-amber-500 inline-block" /> 预约</span>
              <span class="flex items-center gap-1"><span class="w-2 h-2 rounded-full bg-gray-400 inline-block" /> 锁定</span>
            </div>
            <div class="text-xs text-slate-500 dark:text-slate-400 font-mono">
              已筛选 <strong class="text-slate-800 dark:text-slate-200 font-bold">{{ filteredBins.length }}</strong> 个储位
            </div>
          </div>
        </div>

        <!-- Storage Bin Grid Container -->
        <div class="flex flex-col justify-between rounded border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
          <div v-if="paginatedBins.length > 0" class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-6 gap-3">
            <div
              v-for="bin in paginatedBins"
              :key="bin.id"
              class="p-3 rounded-lg border transition-all cursor-pointer select-none relative group hover:scale-[1.02] shadow-sm"
              :class="getBinStatusColor(bin.status)"
              @click="toggleBinStatus(bin)"
            >
              <div class="flex items-center justify-between mb-1">
                <span class="font-mono font-bold text-sm text-slate-900 dark:text-white">{{ bin.id }}</span>
                <span class="text-[10px] font-mono px-1.5 py-0.5 rounded font-semibold bg-white/60 dark:bg-black/40">
                  {{ getBinStatusText(bin.status) }}
                </span>
              </div>

              <div class="text-[11px] text-gray-600 dark:text-gray-300 font-medium truncate mb-1">
                {{ bin.name }}
              </div>

              <div class="text-[10px] text-gray-400 font-mono truncate">
                {{ bin.palletCode || '无载具' }}
              </div>

              <div class="text-[9px] text-gray-400 mt-1 flex justify-between items-center">
                <span>更新: {{ bin.updatedAt }}</span>
                <div class="i-carbon-edit opacity-0 group-hover:opacity-100 transition" />
              </div>
            </div>
          </div>

          <div v-else class="py-20 text-center">
            <NEmpty description="未找到符合筛选条件的仓储库位" size="small" />
          </div>

          <!-- Pagination Footer -->
          <div class="flex flex-wrap items-center justify-between gap-3 pt-3 mt-4 border-t border-slate-100 dark:border-slate-800">
            <div class="text-xs text-slate-500 dark:text-slate-400">
              共 <strong class="text-slate-800 dark:text-slate-200 font-semibold">{{ filteredBins.length }}</strong> 个储位
              <span v-if="filteredBins.length > 0" class="ml-1 text-gray-400">
                (第 {{ binPage }} / {{ Math.ceil(filteredBins.length / binPageSize) || 1 }} 页)
              </span>
            </div>
            <NPagination
              v-model:page="binPage"
              v-model:page-size="binPageSize"
              :item-count="filteredBins.length"
              :page-sizes="[6, 12, 18, 24]"
              show-size-picker
              show-quick-jumper
              size="small"
            />
          </div>
        </div>
      </div>

      <!-- TAB 2: Mutex Collision Zones -->
      <div v-else class="space-y-4 pt-1">
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
          <!-- Left Zone List (2 cols) -->
          <div class="md:col-span-2 space-y-3">
            <div class="p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-200 dark:border-slate-800 flex items-center justify-between">
              <NInput
                v-model:value="zoneSearch"
                placeholder="搜索互斥区 ID / 名称..."
                clearable
                size="small"
                :style="{ width: '220px' }"
              >
                <template #prefix><div class="i-carbon-search text-gray-400" /></template>
              </NInput>
              <div class="text-xs text-slate-500 font-mono">
                已标定 <strong class="text-slate-800 dark:text-slate-200 font-semibold">{{ filteredZones.length }}</strong> 个互斥防冲区
              </div>
            </div>

            <div class="flex flex-col justify-between space-y-2 rounded border border-slate-200 bg-white p-3 dark:border-slate-800 dark:bg-slate-900">
              <div class="space-y-2">
                <div
                  v-for="zone in paginatedZones"
                  :key="zone.id"
                  class="p-3 rounded border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 flex items-center justify-between"
                >
                  <div>
                    <div class="flex items-center gap-2">
                      <span class="font-mono font-bold text-xs text-slate-800 dark:text-slate-200">{{ zone.id }}</span>
                      <NTag size="tiny" type="warning">{{ zone.name }}</NTag>
                      <NTag size="tiny" :type="zone.occupied ? 'error' : 'success'">
                        {{ zone.occupied ? '当前已被占用' : '空闲中' }}
                      </NTag>
                    </div>
                    <div class="text-[11px] text-gray-500 mt-1">
                      最大容纳车辆: <span class="font-bold">{{ zone.maxCapacity }} 台</span> | 超时保护: <span>{{ zone.timeoutSec }}s</span>
                    </div>
                  </div>

                  <div class="text-right text-xs">
                    <NButton size="tiny" secondary type="warning">强制释放特权</NButton>
                  </div>
                </div>

                <div v-if="paginatedZones.length === 0" class="py-12 text-center">
                  <NEmpty description="暂无符合条件的几何互斥区" size="small" />
                </div>
              </div>

              <!-- Zone Pagination Footer -->
              <div class="flex items-center justify-between pt-2 border-t border-slate-100 dark:border-slate-800">
                <span class="text-xs text-gray-400 font-mono">共 {{ filteredZones.length }} 个防冲区</span>
                <NPagination
                  v-model:page="zonePage"
                  v-model:page-size="zonePageSize"
                  :item-count="filteredZones.length"
                  :page-sizes="[2, 4, 8]"
                  size="small"
                />
              </div>
            </div>
          </div>

          <!-- Right Ray-Casting Sandbox (1 col) -->
          <div>
            <NCard title="射线检测" size="small" class="shadow-sm">
              <div class="space-y-3 text-xs">
                <p class="text-gray-500 text-[11px]">
                  用当前地图上的互斥区多边形做点在多边形内判断。坐标单位是米。
                </p>

                <div class="space-y-2">
                  <div class="flex items-center gap-2">
                    <span class="w-12 text-gray-500">X 坐标:</span>
                    <NInputNumber v-model:value="simTestX" size="small" :step="1" class="flex-1" />
                  </div>
                  <div class="flex items-center gap-2">
                    <span class="w-12 text-gray-500">Y 坐标:</span>
                    <NInputNumber v-model:value="simTestY" size="small" :step="1" class="flex-1" />
                  </div>
                  <NButton type="primary" size="small" class="w-full" @click="runRayCastingTest">
                    运行射线交叉检验
                  </NButton>
                </div>

                <div v-if="simResult" class="p-3 rounded border border-slate-200 dark:border-slate-800 bg-slate-50/80 dark:bg-slate-850">
                  <div class="flex items-center justify-between mb-1">
                    <span class="font-semibold text-slate-700 dark:text-slate-300">判定结果:</span>
                    <NTag size="tiny" :type="simResult.inside ? 'error' : 'success'">
                      {{ simResult.inside ? '位于互斥区内部 [锁区拦截]' : '位于互斥区外部 [自由通行]' }}
                    </NTag>
                  </div>
                  <div class="text-[11px] text-gray-400">命中 {{ simResult.intersectionCount }} 个互斥区{{ simResult.names.length ? `：${simResult.names.join('、')}` : '' }}</div>
                </div>
              </div>
            </NCard>
          </div>
        </div>
      </div>
    </NCard>
  </div>
</template>
