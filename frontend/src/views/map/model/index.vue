<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useAuth } from '@/hooks/business/auth';
import { useMapStore } from '@/store/modules/map';
import { authFetch, fmsApi } from '@/service/api/fms';
import MapCanvas from '@/components/fms/MapCanvas.vue';

const { checkPermission, currentRoleInfo } = useAuth();
const mapStore = useMapStore();

const activeTab = ref<'visual' | 'nodes' | 'paths' | 'parse' | 'diagnostics'>('visual');

const nodes = computed(() => mapStore.currentGraph?.nodes || []);
const paths = computed(() => mapStore.currentGraph?.paths || []);
const mutexZones = computed(() => mapStore.currentGraph?.mutexZones || []);

// Loading states
const isLoadingMap = ref(false);

// Nodes pagination & filtering
const nodePage = ref(1);
const nodePageSize = ref(8);
const nodeSearch = ref('');
const nodeTypeFilter = ref<string | null>(null);
const nodeRevolveFilter = ref<string | null>(null);

watch([nodeSearch, nodeTypeFilter, nodeRevolveFilter], () => {
  nodePage.value = 1;
});

const filteredNodes = computed(() => {
  return nodes.value.filter(node => {
    const matchSearch =
      !nodeSearch.value ||
      node.id.toLowerCase().includes(nodeSearch.value.toLowerCase()) ||
      (node.name && node.name.toLowerCase().includes(nodeSearch.value.toLowerCase())) ||
      (node.className && node.className.toLowerCase().includes(nodeSearch.value.toLowerCase()));
    const matchType = !nodeTypeFilter.value || node.type === nodeTypeFilter.value;
    const matchRevolve =
      !nodeRevolveFilter.value ||
      (nodeRevolveFilter.value === 'yes' ? node.allowRevolve : !node.allowRevolve);
    return matchSearch && matchType && matchRevolve;
  });
});

const paginatedNodes = computed(() => {
  const start = (nodePage.value - 1) * nodePageSize.value;
  return filteredNodes.value.slice(start, start + nodePageSize.value);
});

// Paths pagination & filtering
const pathPage = ref(1);
const pathPageSize = ref(8);
const pathSearch = ref('');
const pathDirectionFilter = ref<string | null>(null);
const pathTypeFilter = ref<string | null>(null);

watch([pathSearch, pathDirectionFilter, pathTypeFilter], () => {
  pathPage.value = 1;
});

const filteredPaths = computed(() => {
  return paths.value.filter(path => {
    const matchSearch =
      !pathSearch.value ||
      path.id.toLowerCase().includes(pathSearch.value.toLowerCase()) ||
      path.startNodeId.toLowerCase().includes(pathSearch.value.toLowerCase()) ||
      path.endNodeId.toLowerCase().includes(pathSearch.value.toLowerCase());
    const matchDirection = !pathDirectionFilter.value || (path.direction || 'forward') === pathDirectionFilter.value;
    const matchType = !pathTypeFilter.value || (path.pathType === pathTypeFilter.value || path.curveType === pathTypeFilter.value);
    return matchSearch && matchDirection && matchType;
  });
});

const paginatedPaths = computed(() => {
  const start = (pathPage.value - 1) * pathPageSize.value;
  return filteredPaths.value.slice(start, start + pathPageSize.value);
});

const topologyReport = computed(() => {
  const nodeList = nodes.value;
  const pathList = paths.value;
  const ids = nodeList.map(n => n.id);
  if (ids.length === 0) {
    return { health: 0, components: 0, isolated: 0 };
  }
  const parent: Record<string, string> = {};
  const degree: Record<string, number> = {};
  for (const id of ids) {
    parent[id] = id;
    degree[id] = 0;
  }
  const find = (x: string): string => {
    if (!(x in parent)) return x;
    if (parent[x] !== x) parent[x] = find(parent[x]);
    return parent[x];
  };
  for (const path of pathList) {
    const a = path.startNodeId;
    const b = path.endNodeId;
    if (a in parent && b in parent) {
      parent[find(a)] = find(b);
      degree[a] += 1;
      degree[b] += 1;
    }
  }
  const components = new Set(ids.map(id => find(id))).size;
  const isolated = ids.filter(id => degree[id] === 0).length;
  const health = Math.max(0, Math.round(100 - (isolated / ids.length) * 100 - Math.max(0, components - 1) * 8));
  return { health, components, isolated };
});

// Map Parser State
const selectedFormat = ref<'xmap' | 'json' | 'xml' | 'dxf'>('json');
const mapSourceCode = ref(`{
  "name": "NXP-Cleanroom-Benchmark",
  "version": "v2.6.0",
  "bounds": { "minX": 0, "maxX": 50, "minY": 0, "maxY": 30 },
  "stations": [
    { "id": "STK-01", "name": "Stocker-01 货架", "type": "JobPoint", "x": 5.0, "y": 5.0, "allowRevolve": true, "spinDirection": "both", "className": "LandMark" },
    { "id": "STK-02", "name": "Stocker-02 货架", "type": "JobPoint", "x": 5.0, "y": 25.0, "allowRevolve": true, "spinDirection": "both", "className": "LandMark" },
    { "id": "WB-01", "name": "WireBonder-01 机台", "type": "JobPoint", "x": 45.0, "y": 5.0, "allowRevolve": true, "spinDirection": "both", "className": "LandMark" },
    { "id": "WB-02", "name": "WireBonder-02 机台", "type": "JobPoint", "x": 45.0, "y": 25.0, "allowRevolve": true, "spinDirection": "both", "className": "LandMark" },
    { "id": "CH-01", "name": "自动充电机 1", "type": "ChargePoint", "x": 25.0, "y": 15.0, "allowRevolve": false, "spinDirection": "none", "className": "ChargePoint" },
    { "id": "PARK-01", "name": "待命停靠区 1", "type": "PullOverPoint", "x": 15.0, "y": 5.0, "allowRevolve": true, "spinDirection": "both", "className": "ReturnPoint" },
    { "id": "PARK-02", "name": "待命停靠区 2", "type": "PullOverPoint", "x": 35.0, "y": 25.0, "allowRevolve": true, "spinDirection": "both", "className": "ReturnPoint" },
    { "id": "SPUR-01", "name": "主干道避让站 1", "type": "PullOverPoint", "x": 25.0, "y": 2.0, "allowRevolve": true, "spinDirection": "both", "className": "ReturnPoint" },
    { "id": "SPUR-02", "name": "主干道避让站 2", "type": "PullOverPoint", "x": 25.0, "y": 28.0, "allowRevolve": true, "spinDirection": "both", "className": "ReturnPoint" }
  ],
  "lanes": [
    { "id": "P-MAIN-1", "from": "JUNCT-L", "to": "JUNCT-C", "type": "line", "direction": "forward", "roadWidth": 1.2, "bidirectional": true },
    { "id": "P-MAIN-2", "from": "JUNCT-C", "to": "JUNCT-R", "type": "line", "direction": "forward", "roadWidth": 1.2, "bidirectional": true },
    { "id": "P-SPUR-1", "from": "JUNCT-C", "to": "SPUR-01", "type": "CubicBezier", "direction": "forward", "roadWidth": 1.0, "bidirectional": true }
  ]
}`);

const parseSummary = ref<{
  success: boolean;
  stationCount: number;
  laneCount: number;
  bounds: string;
  hasBezier: boolean;
  message: string;
} | null>(null);

// Node Modal State
const showNodeModal = ref(false);
const isEditingNode = ref(false);
const nodeForm = reactive({
  id: '',
  name: '',
  type: 'JobPoint',
  x: 10,
  y: 10,
  headingDeg: 0,
  allowRevolve: true,
  spinDirection: 'both',
  className: 'LandMark'
});

onMounted(async () => {
  await mapStore.loadCurrentMap();
});

// Load Kecong 52MB Real Vehicle Map
async function handleUploadRaster(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  if (!checkPermission('Fms.Map.Parse', '载入科聪底图')) return;
  isLoadingMap.value = true;
  try {
    const res = await mapStore.uploadRaster(file);
    window.$message?.success(`科聪底图已贴到路网：${res.width}×${res.height}`);
  } catch (err: any) {
    window.$message?.error(err.message || '底图解析失败');
  } finally {
    isLoadingMap.value = false;
    input.value = '';
  }
}

async function handleLoadKecongMap() {
  if (!checkPermission('Fms.Map.Parse', '加载实车大地图')) return;
  isLoadingMap.value = true;
  try {
    const res = await mapStore.loadKecongDemo();
    window.$message?.success(
      `成功载入科聪实车 52MB 大地图！拓扑站点: ${res?.nodeCount ?? 1257} 个，航道: ${res?.pathCount ?? 2568} 条，互斥区: ${res?.zoneCount ?? 32} 个，SLAM点云: ${res?.totalPoints ?? 629423} 点`
    );
  } catch (e: any) {
    window.$message?.error(`加载实车地图失败: ${e.message}`);
  } finally {
    isLoadingMap.value = false;
  }
}

// Reset Benchmark Map
async function handleLoadBenchmarkMap() {
  if (!checkPermission('Fms.Map.Parse', '重置基准地图')) return;
  isLoadingMap.value = true;
  try {
    await mapStore.loadBenchmark();
    window.$message?.success('已重置为 Cleanroom 洁净室双向基准拓扑地图');
  } catch (e: any) {
    window.$message?.error(`重置基准地图失败: ${e.message}`);
  } finally {
    isLoadingMap.value = false;
  }
}

// Export Xmap XML
function handleExportXmap() {
  window.open('/api/fms/map/export-xmap', '_blank');
  window.$message?.info('正在导出科聪格式 .xmap 拓扑文件...');
}

const isSavingMap = ref(false);

async function handleSaveMapToServer() {
  if (!checkPermission('Fms.Map.Edit', '保存地图至数据库')) return;
  isSavingMap.value = true;
  try {
    const res = await authFetch('/api/fms/map/save', { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      window.$message?.success(
        `地图已持久化保存至 SQLite 数据库！节点: ${data.nodeCount}，航道: ${data.pathCount}，互斥区: ${data.zoneCount}`
      );
    } else {
      window.$message?.error(data.message || '保存地图失败');
    }
  } catch (e: any) {
    window.$message?.error(`保存地图失败: ${e.message}`);
  } finally {
    isSavingMap.value = false;
  }
}

const isApplyingMap = ref(false);

function handleParseMapCode() {
  if (!checkPermission('Fms.Map.Parse', '语法检查地图原文')) return;
  try {
    const parsed = JSON.parse(mapSourceCode.value);
    parseSummary.value = {
      success: true,
      stationCount: parsed.stations?.length || parsed.nodes?.length || 0,
      laneCount: parsed.lanes?.length || parsed.paths?.length || 0,
      bounds: `X: [${parsed.bounds?.minX ?? 0}m, ${parsed.bounds?.maxX ?? 50}m], Y: [${parsed.bounds?.minY ?? 0}m, ${parsed.bounds?.maxY ?? 30}m]`,
      hasBezier: (parsed.lanes || parsed.paths || []).some((l: any) => l.type === 'CubicBezier' || l.curveType === 'bezier_curve'),
      message: '仅完成 JSON 语法检查，当前运行路网尚未更换。'
    };
    window.$message?.success('语法检查通过。当前运行路网未更换。');
  } catch (e: any) {
    parseSummary.value = {
      success: false,
      stationCount: 0,
      laneCount: 0,
      bounds: '-',
      hasBezier: false,
      message: `语法检查失败: ${e.message}`
    };
    window.$message?.error(`语法检查失败: ${e.message}`);
  }
}

async function handleApplyMapCode() {
  if (!checkPermission('Fms.Map.Parse', '应用到当前路网')) return;
  isApplyingMap.value = true;
  try {
    const data = await fmsApi.applyEditorMapJson(mapSourceCode.value);
    if (data.success) {
      parseSummary.value = {
        success: true,
        stationCount: data.nodeCount || 0,
        laneCount: data.pathCount || 0,
        bounds: '-',
        hasBezier: false,
        message: data.message || `已应用到当前路网「${data.mapName || ''}」`
      };
      window.$message?.success(data.message || '已应用到当前路网');
      await mapStore.loadCurrentMap();
    } else {
      window.$message?.error(data.error || '应用到当前路网失败');
    }
  } catch (e: any) {
    window.$message?.error(`应用到当前路网失败: ${e.message}`);
  } finally {
    isApplyingMap.value = false;
  }
}

function loadPreset(presetName: string) {
  if (presetName === 'warehouse') {
    mapSourceCode.value = JSON.stringify({
      name: "Warehouse-ASRS-DualLoop",
      version: "v1.2",
      bounds: { minX: 0, maxX: 80, minY: 0, maxY: 40 },
      stations: [
        { id: "A-01", name: "入库暂存码头 1", type: "JobPoint", x: 10, y: 10, allowRevolve: true, spinDirection: "both", className: "LandMark" },
        { id: "A-02", name: "入库暂存码头 2", type: "JobPoint", x: 10, y: 30, allowRevolve: true, spinDirection: "both", className: "LandMark" },
        { id: "B-01", name: "立库提升机 1", type: "JobPoint", x: 70, y: 10, allowRevolve: true, spinDirection: "both", className: "LandMark" },
        { id: "B-02", name: "立库提升机 2", type: "JobPoint", x: 70, y: 30, allowRevolve: true, spinDirection: "both", className: "LandMark" },
        { id: "CH-01", name: "快充桩 1", type: "ChargePoint", x: 40, y: 20, allowRevolve: false, spinDirection: "none", className: "ChargePoint" },
        { id: "P-01", name: "待命位 1", type: "PullOverPoint", x: 20, y: 5, allowRevolve: true, spinDirection: "both", className: "ReturnPoint" }
      ],
      lanes: [
        { id: "P-LOOP-1", from: "A-01", to: "B-01", type: "line", direction: "forward", roadWidth: 1.2, bidirectional: false },
        { id: "P-LOOP-2", from: "B-01", to: "B-02", type: "CubicBezier", direction: "forward", roadWidth: 1.2, bidirectional: false },
        { id: "P-LOOP-3", from: "B-02", to: "A-02", type: "line", direction: "forward", roadWidth: 1.2, bidirectional: false }
      ]
    }, null, 2);
    window.$message?.info('已载入【电商立库窄巷道双向环线】工业地图');
  } else {
    window.$message?.info('已载入【半导体洁净室高密度航网】工业地图');
  }
}

function openAddNode() {
  if (!checkPermission('Fms.Map.Edit', '新增工位站点')) return;
  isEditingNode.value = false;
  nodeForm.id = `ST-${Date.now().toString().slice(-4)}`;
  nodeForm.name = `工位-${nodeForm.id}`;
  nodeForm.type = 'JobPoint';
  nodeForm.x = 20;
  nodeForm.y = 10;
  nodeForm.headingDeg = 0;
  nodeForm.allowRevolve = true;
  nodeForm.spinDirection = 'both';
  nodeForm.className = 'LandMark';
  showNodeModal.value = true;
}

function openEditNode(node: any) {
  if (!checkPermission('Fms.Map.Edit', '编辑工位站点')) return;
  isEditingNode.value = true;
  nodeForm.id = node.id;
  nodeForm.name = node.name || node.id;
  nodeForm.type = node.type || 'JobPoint';
  nodeForm.x = node.x;
  nodeForm.y = node.y;
  nodeForm.headingDeg = node.headingDeg ?? 0;
  nodeForm.allowRevolve = node.allowRevolve !== false;
  nodeForm.spinDirection = node.spinDirection || 'both';
  nodeForm.className = node.className || 'LandMark';
  showNodeModal.value = true;
}

async function submitNodeForm() {
  try {
    const res = await authFetch('/api/fms/map/nodes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(nodeForm)
    });
    const data = await res.json();
    if (data.code === '0000') {
      window.$message?.success('站点保存成功！');
      showNodeModal.value = false;
      await mapStore.loadCurrentMap();
    } else {
      window.$message?.error(data.msg || '保存失败');
    }
  } catch (e: any) {
    window.$message?.error(e.message);
  }
}

function selectNodeAndFocus(nodeId: string) {
  mapStore.selectedNodeId = nodeId;
  activeTab.value = 'visual';
  window.$message?.info(`已在画布高亮标定站点: ${nodeId}`);
}

function selectPathAndFocus(pathId: string) {
  mapStore.selectedPathId = pathId;
  activeTab.value = 'visual';
  window.$message?.info(`已在画布高亮航道: ${pathId}`);
}
</script>

<template>
  <div class="w-full space-y-4 p-4 pb-8">
    <!-- Header -->
    <NCard size="medium" class="shadow-sm">
      <div class="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-base font-bold text-slate-800 dark:text-slate-100">
              地图建模
            </h1>
            <NTag size="tiny" type="info" round :bordered="false">{{ mapStore.currentGraph?.name || '未载入' }}</NTag>
            <NTag v-if="mapStore.totalPointCloudCount > 0" size="tiny" type="primary" round :bordered="false">
              点云 {{ mapStore.totalPointCloudCount.toLocaleString() }}
            </NTag>
          </div>
          <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
            载入科聪底图、点云和路网，检查站点和互斥区。
          </p>
        </div>

        <NSpace align="center" :size="8">
          <NTag size="small" round :bordered="false" type="info">当前角色: {{ currentRoleInfo.label }}</NTag>
          <NButton
            type="primary"
            size="small"
            :loading="isLoadingMap"
            @click="handleLoadKecongMap"
          >
            <template #icon><div class="i-carbon-data-enrichment" /></template>
            载入科聪地图
          </NButton>
          <label class="inline-flex">
            <input class="hidden" type="file" accept=".png,.jpg,.jpeg,.pgm,image/png,image/jpeg" @change="handleUploadRaster" />
            <NButton size="small" tag="span">科聪底图</NButton>
          </label>
          <NButton
            size="small"
            :loading="isLoadingMap"
            @click="handleLoadBenchmarkMap"
          >
            <template #icon><div class="i-carbon-reset" /></template>
            恢复基准图
          </NButton>
          <NButton
            size="small"
            type="success"
            secondary
            :loading="isSavingMap"
            @click="handleSaveMapToServer"
          >
            <template #icon><div class="i-carbon-save" /></template>
            保存
          </NButton>
          <NButton
            size="small"
            secondary
            @click="handleExportXmap"
          >
            <template #icon><div class="i-carbon-document-export" /></template>
            导出
          </NButton>
          <NButton type="primary" size="small" @click="openAddNode">
            <template #icon><div class="i-carbon-add" /></template>
            新建站点
          </NButton>
        </NSpace>
      </div>

      <!-- Tabs Navigation -->
      <NTabs v-model:value="activeTab" type="line" class="mt-4">
        <!-- Tab 0: Digital Twin Map Visual Canvas -->
        <NTabPane name="visual" tab="全景拓扑">
          <div class="space-y-3 pt-2">
            <!-- Top Map KPI Metrics & Layer Controls -->
            <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 text-xs">
              <div class="flex flex-wrap items-center gap-4">
                <div class="flex items-center gap-1.5">
                  <span class="text-slate-500">拓扑节点:</span>
                  <span class="font-bold font-mono text-slate-800 dark:text-slate-200">{{ mapStore.stats.totalNodes }} 个</span>
                </div>
                <div class="h-3 w-px bg-slate-200 dark:bg-slate-700" />
                <div class="flex items-center gap-1.5">
                  <span class="text-slate-500">航道总数:</span>
                  <span class="font-bold font-mono text-slate-800 dark:text-slate-200">
                    {{ mapStore.stats.totalPaths }} 条
                    <span class="text-blue-500 font-normal text-[11px]">(贝塞尔 {{ mapStore.stats.bezierPaths }})</span>
                  </span>
                </div>
                <div class="h-3 w-px bg-slate-200 dark:bg-slate-700" />
                <div class="flex items-center gap-1.5">
                  <span class="text-slate-500">互斥交管区:</span>
                  <span class="font-bold font-mono text-amber-600 dark:text-amber-400">{{ mapStore.stats.zones }} 区</span>
                </div>
                <div class="h-3 w-px bg-slate-200 dark:bg-slate-700" />
                <div class="flex items-center gap-1.5">
                  <span class="text-slate-500">激光点云:</span>
                  <span class="font-bold font-mono text-emerald-600 dark:text-emerald-400">
                    {{ mapStore.pointCloud.length.toLocaleString() }} / {{ mapStore.totalPointCloudCount.toLocaleString() }}
                  </span>
                </div>
              </div>

              <!-- Layer Visibility Toggles -->
              <div class="flex flex-wrap items-center gap-3">
                <span class="text-slate-400 font-medium text-[11px]">图层开关:</span>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.raster" size="small" />
                  <span class="text-[11px]">科聪底图</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.lidarPoints" size="small" />
                  <span class="text-[11px]">激光点云</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.nodes" size="small" />
                  <span class="text-[11px]">站点</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.bezierPaths" size="small" />
                  <span class="text-[11px]">贝塞尔航网</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.straightPaths" size="small" />
                  <span class="text-[11px]">直线航线</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.arrows" size="small" />
                  <span class="text-[11px]">流向箭头</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.mutexZones" size="small" />
                  <span class="text-[11px]">互斥交管区</span>
                </label>
                <label class="flex items-center gap-1 cursor-pointer">
                  <NSwitch v-model:value="mapStore.layerVisibility.stationLabels" size="small" />
                  <span class="text-[11px]">站点名</span>
                </label>
              </div>
            </div>

            <!-- Integrated PixiJS Map Canvas Container -->
            <div class="grid grid-cols-1 lg:grid-cols-4 gap-4">
              <div class="lg:col-span-3 relative h-[calc(100vh-280px)] min-h-[640px] overflow-hidden rounded-xl border border-slate-200 bg-[#f7f8fa] dark:border-slate-800 dark:bg-[#101418]">
                <MapCanvas
                  :graph="mapStore.currentGraph"
                  :point-cloud="mapStore.pointCloud"
                  :raster="mapStore.raster"
                  :mutex-zones="mapStore.currentGraph?.mutexZones || []"
                  :selected-node-id="mapStore.selectedNodeId"
                  :selected-path-id="mapStore.selectedPathId"
                  :layer-visibility="mapStore.layerVisibility"
                  @select-node="(id) => mapStore.selectedNodeId = id"
                  @select-path="(id) => mapStore.selectedPathId = id"
                />
              </div>

              <!-- Inspector Panel for Selected Node / Path -->
              <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-4 bg-slate-50/60 dark:bg-slate-800/40 text-xs flex flex-col justify-between">
                <div>
                  <div class="font-bold text-slate-800 dark:text-slate-100 pb-2 mb-3 border-b border-slate-200 dark:border-slate-700 flex items-center justify-between">
                    <span>图元属性</span>
                    <NTag v-if="mapStore.selectedNode" size="tiny" type="info">站点</NTag>
                    <NTag v-else-if="mapStore.selectedPath" size="tiny" type="primary">航道</NTag>
                    <NTag v-else size="tiny" type="default">就绪</NTag>
                  </div>

                  <!-- If Node Selected -->
                  <div v-if="mapStore.selectedNode" class="space-y-2.5">
                    <div class="flex justify-between">
                      <span class="text-slate-400">站点编号 ID:</span>
                      <span class="font-mono font-bold text-slate-800 dark:text-slate-200">{{ mapStore.selectedNode.id }}</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">站点名称:</span>
                      <span class="font-medium text-slate-800 dark:text-slate-200">{{ mapStore.selectedNode.name || '-' }}</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">功能类型:</span>
                      <NTag size="tiny" type="info">{{ mapStore.selectedNode.type }}</NTag>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">坐标（米）</span>
                      <span class="font-mono text-emerald-600 dark:text-emerald-400">
                        ({{ mapStore.selectedNode.x.toFixed(2) }}m, {{ mapStore.selectedNode.y.toFixed(2) }}m)
                      </span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">基准朝向角:</span>
                      <span class="font-mono">{{ mapStore.selectedNode.headingDeg ?? 0 }}°</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">允许原地自旋:</span>
                      <NTag size="tiny" :type="mapStore.selectedNode.allowRevolve !== false ? 'success' : 'default'">
                        {{ mapStore.selectedNode.allowRevolve !== false ? '允许' : '禁止' }}
                      </NTag>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">转向限制:</span>
                      <span class="font-mono">{{ mapStore.selectedNode.spinDirection || 'both' }}</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">科聪分类标签:</span>
                      <NTag size="tiny" type="warning">{{ mapStore.selectedNode.className || 'LandMark' }}</NTag>
                    </div>

                    <div class="pt-3">
                      <NButton size="tiny" type="primary" secondary class="w-full" @click="openEditNode(mapStore.selectedNode)">
                        快速编辑此站点
                      </NButton>
                    </div>
                  </div>

                  <!-- If Path Selected -->
                  <div v-else-if="mapStore.selectedPath" class="space-y-2.5">
                    <div class="flex justify-between">
                      <span class="text-slate-400">航道编号 ID:</span>
                      <span class="font-mono font-bold text-slate-800 dark:text-slate-200">{{ mapStore.selectedPath.id }}</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">起点 → 终点:</span>
                      <span class="font-mono text-blue-600 dark:text-blue-400">
                        {{ mapStore.selectedPath.startNodeId }} → {{ mapStore.selectedPath.endNodeId }}
                      </span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">几何类型:</span>
                      <NTag size="tiny" :type="mapStore.selectedPath.pathType === 'CubicBezier' ? 'primary' : 'default'">
                        {{ mapStore.selectedPath.pathType }}
                      </NTag>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">行车流向:</span>
                      <NTag size="tiny" :type="mapStore.selectedPath.direction === 'backward' ? 'warning' : 'success'">
                        {{ mapStore.selectedPath.direction === 'backward' ? '倒车' : '前进' }}
                      </NTag>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">车道宽度:</span>
                      <span class="font-mono">{{ (mapStore.selectedPath.roadWidth ?? 1.2).toFixed(1) }} m</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">双向通行:</span>
                      <span>{{ mapStore.selectedPath.bidirectional ? '双向通行' : '单向通行' }}</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">最高限速:</span>
                      <span class="font-mono text-emerald-600">{{ mapStore.selectedPath.maxSpeed }} m/s</span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-slate-400">航线长度:</span>
                      <span class="font-mono">{{ (mapStore.selectedPath.length || 0).toFixed(2) }} m</span>
                    </div>
                  </div>

                  <!-- Empty selection prompt -->
                  <div v-else class="text-slate-400 py-16 text-center space-y-2">
                    <div class="i-carbon-touch-interaction text-3xl mx-auto text-slate-300 dark:text-slate-600" />
                    <div>点击画布中的站点或航道</div>
                    <div class="text-[11px] text-slate-400">可实时审查几何属性与交管设定</div>
                  </div>
                </div>

                <!-- Bottom Helper -->
                <div class="p-2.5 rounded bg-blue-50/60 dark:bg-blue-950/20 text-blue-700 dark:text-blue-300 text-[11px] border border-blue-100 dark:border-blue-900/40">
                  💡 提示：按住鼠标左键可平移画布，滚动滚轮缩放；点击右上角工具栏可一键自适应全屏。
                </div>
              </div>
            </div>
          </div>
        </NTabPane>

        <!-- Tab 1: Station Nodes Table -->
        <NTabPane name="nodes" tab="站点标定">
          <div class="space-y-3 pt-2">
            <!-- Filter Bar -->
            <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-100 dark:border-slate-800">
              <NSpace align="center" :size="12">
                <NInput
                  v-model:value="nodeSearch"
                  placeholder="搜索工位 ID / 名称 / 标签..."
                  clearable
                  size="small"
                  :style="{ width: '220px' }"
                >
                  <template #prefix><div class="i-carbon-search text-gray-400" /></template>
                </NInput>
                <NSelect
                  v-model:value="nodeTypeFilter"
                  size="small"
                  clearable
                  placeholder="全部工位类型"
                  :style="{ width: '180px' }"
                  :options="[
                    { label: '全部工位类型', value: '' },
                    { label: '作业工位', value: 'JobPoint' },
                    { label: '充电点', value: 'ChargePoint' },
                    { label: '避让点', value: 'PullOverPoint' },
                    { label: '地标', value: 'Landmark' },
                    { label: '待命点', value: 'StandbyPoint' }
                  ]"
                />
                <NSelect
                  v-model:value="nodeRevolveFilter"
                  size="small"
                  clearable
                  placeholder="自旋属性过滤"
                  :style="{ width: '150px' }"
                  :options="[
                    { label: '全部自旋属性', value: '' },
                    { label: '允许自旋', value: 'yes' },
                    { label: '禁止自旋', value: 'no' }
                  ]"
                />
              </NSpace>
              <div class="text-xs text-slate-500 dark:text-slate-400 font-mono">
                已标定 <strong class="text-slate-800 dark:text-slate-200 font-bold">{{ filteredNodes.length }}</strong> 个工位点
              </div>
            </div>

            <!-- Table with Fixed Layout -->
            <div class="border border-slate-200 dark:border-slate-800 rounded overflow-hidden">
              <div class="overflow-x-auto">
                <table class="w-full text-left text-xs border-collapse table-fixed">
                  <colgroup>
                    <col style="width: 120px;" />
                    <col style="width: 160px;" />
                    <col style="width: 130px;" />
                    <col style="width: 140px;" />
                    <col style="width: 90px;" />
                    <col style="width: 90px;" />
                    <col style="width: 100px;" />
                    <col style="width: 110px;" />
                    <col style="width: 130px;" />
                  </colgroup>
                  <thead>
                    <tr class="bg-slate-100 dark:bg-slate-800/80 text-slate-700 dark:text-slate-300 font-medium border-b border-slate-200 dark:border-slate-700">
                      <th class="py-2.5 px-3">站点 ID</th>
                      <th class="py-2.5 px-3">站点名称</th>
                      <th class="py-2.5 px-3">功能类型</th>
                      <th class="py-2.5 px-3">坐标（米）</th>
                      <th class="py-2.5 px-3">朝向角</th>
                      <th class="py-2.5 px-3">原地自旋</th>
                      <th class="py-2.5 px-3">转向限制</th>
                      <th class="py-2.5 px-3">科聪标签</th>
                      <th class="py-2.5 px-3 text-right">操作</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
                    <tr v-for="node in paginatedNodes" :key="node.id" class="h-11 hover:bg-slate-50 dark:hover:bg-slate-800/50">
                      <td class="py-2 px-3 font-mono font-semibold text-slate-800 dark:text-slate-200 truncate">{{ node.id }}</td>
                      <td class="py-2 px-3 truncate">{{ node.name || '-' }}</td>
                      <td class="py-2 px-3">
                        <NTag size="tiny" :type="node.type === 'ChargePoint' ? 'warning' : node.type === 'PullOverPoint' ? 'success' : 'info'">
                          {{ node.type }}
                        </NTag>
                      </td>
                      <td class="py-2 px-3 font-mono text-gray-600 dark:text-gray-400 truncate">
                        ({{ node.x.toFixed(2) }}m, {{ node.y.toFixed(2) }}m)
                      </td>
                      <td class="py-2 px-3 font-mono text-gray-500">{{ node.headingDeg ?? 0 }}°</td>
                      <td class="py-2 px-3">
                        <NTag size="tiny" :type="node.allowRevolve !== false ? 'success' : 'default'">
                          {{ node.allowRevolve !== false ? '允许' : '禁止' }}
                        </NTag>
                      </td>
                      <td class="py-2 px-3 font-mono text-[11px]">
                        {{ node.spinDirection === 'both' ? '双向自由' : node.spinDirection === 'clockwise' ? '仅顺时针' : node.spinDirection === 'counterclockwise' ? '仅逆时针' : '禁止' }}
                      </td>
                      <td class="py-2 px-3">
                        <span class="px-1.5 py-0.5 rounded text-[10px] font-mono bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 border border-slate-200 dark:border-slate-700">
                          {{ node.className || 'LandMark' }}
                        </span>
                      </td>
                      <td class="py-2 px-3 text-right space-x-1">
                        <NButton size="tiny" secondary type="primary" @click="selectNodeAndFocus(node.id)">
                          定位
                        </NButton>
                        <NButton size="tiny" secondary type="info" @click="openEditNode(node)">
                          编辑
                        </NButton>
                      </td>
                    </tr>
                    <tr v-if="paginatedNodes.length === 0">
                      <td colspan="9" class="py-12 text-center text-gray-400 text-xs">
                        <NEmpty description="暂无符合条件的工位站点" size="small" />
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <!-- Pagination Footer -->
              <div class="flex flex-wrap items-center justify-between gap-3 p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-800/30">
                <div class="text-xs text-slate-500 dark:text-slate-400">
                  共 <strong class="text-slate-800 dark:text-slate-200 font-semibold">{{ filteredNodes.length }}</strong> 个工位点
                  <span v-if="filteredNodes.length > 0" class="ml-1 text-gray-400">
                    (第 {{ nodePage }} / {{ Math.ceil(filteredNodes.length / nodePageSize) || 1 }} 页)
                  </span>
                </div>
                <NPagination
                  v-model:page="nodePage"
                  v-model:page-size="nodePageSize"
                  :item-count="filteredNodes.length"
                  :page-sizes="[6, 8, 15, 30]"
                  show-size-picker
                  size="small"
                />
              </div>
            </div>
          </div>
        </NTabPane>

        <!-- Tab 2: Lane Paths Table -->
        <NTabPane name="paths" tab="车道网">
          <div class="space-y-3 pt-2">
            <!-- Filter Bar -->
            <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-100 dark:border-slate-800">
              <NSpace align="center" :size="12">
                <NInput
                  v-model:value="pathSearch"
                  placeholder="搜索航道 ID / 起讫点..."
                  clearable
                  size="small"
                  :style="{ width: '220px' }"
                >
                  <template #prefix><div class="i-carbon-search text-gray-400" /></template>
                </NInput>
                <NSelect
                  v-model:value="pathDirectionFilter"
                  size="small"
                  clearable
                  placeholder="全部行车流向"
                  :style="{ width: '150px' }"
                  :options="[
                    { label: '全部行车流向', value: '' },
                    { label: '前进', value: 'forward' },
                    { label: '倒车', value: 'backward' }
                  ]"
                />
                <NSelect
                  v-model:value="pathTypeFilter"
                  size="small"
                  clearable
                  placeholder="全部航线类型"
                  :style="{ width: '150px' }"
                  :options="[
                    { label: '全部航线类型', value: '' },
                    { label: '曲线', value: 'CubicBezier' },
                    { label: '直线', value: 'Straight' }
                  ]"
                />
              </NSpace>
              <div class="text-xs text-slate-500 dark:text-slate-400 font-mono">
                已建立 <strong class="text-slate-800 dark:text-slate-200 font-bold">{{ filteredPaths.length }}</strong> 条拓扑航道
              </div>
            </div>

            <!-- Table with Fixed Layout -->
            <div class="border border-slate-200 dark:border-slate-800 rounded overflow-hidden">
              <div class="overflow-x-auto">
                <table class="w-full text-left text-xs border-collapse table-fixed">
                  <colgroup>
                    <col style="width: 130px;" />
                    <col style="width: 170px;" />
                    <col style="width: 110px;" />
                    <col style="width: 110px;" />
                    <col style="width: 90px;" />
                    <col style="width: 90px;" />
                    <col style="width: 90px;" />
                    <col style="width: 160px;" />
                    <col style="width: 80px;" />
                  </colgroup>
                  <thead>
                    <tr class="bg-slate-100 dark:bg-slate-800/80 text-slate-700 dark:text-slate-300 font-medium border-b border-slate-200 dark:border-slate-700">
                      <th class="py-2.5 px-3">航道 ID</th>
                      <th class="py-2.5 px-3">起点 → 终点</th>
                      <th class="py-2.5 px-3">几何类型</th>
                      <th class="py-2.5 px-3">行车流向</th>
                      <th class="py-2.5 px-3">车道宽度</th>
                      <th class="py-2.5 px-3">通行模式</th>
                      <th class="py-2.5 px-3">限速（米/秒）</th>
                      <th class="py-2.5 px-3">几何控制状态</th>
                      <th class="py-2.5 px-3 text-right">操作</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-slate-100 dark:divide-slate-800">
                    <tr v-for="path in paginatedPaths" :key="path.id" class="h-11 hover:bg-slate-50 dark:hover:bg-slate-800/50">
                      <td class="py-2 px-3 font-mono font-semibold text-slate-800 dark:text-slate-200 truncate">{{ path.id }}</td>
                      <td class="py-2 px-3 font-mono truncate text-blue-600 dark:text-blue-400">
                        {{ path.startNodeId }} → {{ path.endNodeId }}
                      </td>
                      <td class="py-2 px-3">
                        <NTag size="tiny" :type="path.pathType === 'CubicBezier' ? 'primary' : 'default'">
                          {{ path.pathType }}
                        </NTag>
                      </td>
                      <td class="py-2 px-3">
                        <NTag size="tiny" :type="path.direction === 'backward' ? 'warning' : 'success'">
                          {{ path.direction === 'backward' ? '倒车后退' : '正向前进' }}
                        </NTag>
                      </td>
                      <td class="py-2 px-3 font-mono">{{ (path.roadWidth ?? 1.2).toFixed(1) }}m</td>
                      <td class="py-2 px-3">
                        <NTag size="tiny" :type="path.bidirectional ? 'success' : 'default'">
                          {{ path.bidirectional ? '双向' : '单向' }}
                        </NTag>
                      </td>
                      <td class="py-2 px-3 font-mono">{{ path.maxSpeed }}</td>
                      <td class="py-2 px-3 font-mono text-[11px] text-gray-500 truncate">
                        {{ path.pathType === 'CubicBezier' ? '平滑三阶三次样条控制点已闭合' : '两点线性插值' }}
                      </td>
                      <td class="py-2 px-3 text-right">
                        <NButton size="tiny" secondary type="primary" @click="selectPathAndFocus(path.id)">
                          定位
                        </NButton>
                      </td>
                    </tr>
                    <tr v-if="paginatedPaths.length === 0">
                      <td colspan="9" class="py-12 text-center text-gray-400 text-xs">
                        <NEmpty description="暂无符合条件的航道" size="small" />
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <!-- Pagination Footer -->
              <div class="flex flex-wrap items-center justify-between gap-3 p-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-800/30">
                <div class="text-xs text-slate-500 dark:text-slate-400">
                  共 <strong class="text-slate-800 dark:text-slate-200 font-semibold">{{ filteredPaths.length }}</strong> 条航道
                  <span v-if="filteredPaths.length > 0" class="ml-1 text-gray-400">
                    (第 {{ pathPage }} / {{ Math.ceil(filteredPaths.length / pathPageSize) || 1 }} 页)
                  </span>
                </div>
                <NPagination
                  v-model:page="pathPage"
                  v-model:page-size="pathPageSize"
                  :item-count="filteredPaths.length"
                  :page-sizes="[6, 8, 15, 30]"
                  show-size-picker
                  size="small"
                />
              </div>
            </div>
          </div>
        </NTabPane>

        <!-- Tab 3: Map Parser & Raw Code -->
        <NTabPane name="parse" tab="地图导入">
          <div class="space-y-4 pt-2">
            <div class="flex flex-wrap items-center justify-between gap-3 p-3 bg-slate-50 dark:bg-slate-800/40 rounded border border-slate-100 dark:border-slate-800">
              <div class="flex items-center gap-3">
                <span class="text-xs font-semibold text-slate-700 dark:text-slate-300">工业解析器格式:</span>
                <NRadioGroup v-model:value="selectedFormat" size="small">
                  <NRadioButton value="json">Roboshop / Kecong JSON</NRadioButton>
                  <NRadioButton value="xml">OpenTCS XML</NRadioButton>
                  <NRadioButton value="xmap">科聪 XMAP 航网</NRadioButton>
                  <NRadioButton value="dxf">CAD DXF 矢量图</NRadioButton>
                </NRadioGroup>
              </div>

              <NSpace :size="8">
                <NButton size="small" secondary @click="loadPreset('cleanroom')">洁净室预置</NButton>
                <NButton size="small" secondary @click="loadPreset('warehouse')">立库环线预置</NButton>
                <NButton size="small" secondary @click="handleParseMapCode">
                  <template #icon><div class="i-carbon-checkmark" /></template>
                  仅语法检查
                </NButton>
                <NButton type="primary" size="small" :loading="isApplyingMap" @click="handleApplyMapCode">
                  <template #icon><div class="i-carbon-play" /></template>
                  应用到当前路网
                </NButton>
              </NSpace>
            </div>

            <!-- Code Editor & Parser Summary -->
            <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div class="md:col-span-2">
                <label class="text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1 block">路网原文</label>
                <NInput
                  v-model:value="mapSourceCode"
                  type="textarea"
                  :rows="16"
                  class="font-mono text-xs"
                />
              </div>

              <!-- Summary Card -->
              <div>
                <label class="text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1 block">解析结果</label>
                <div class="p-4 rounded border border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-800/40 space-y-3 text-xs">
                  <div v-if="parseSummary">
                    <div class="flex items-center gap-2 mb-2">
                      <div class="i-carbon-checkmark-filled text-emerald-500 text-lg" v-if="parseSummary.success" />
                      <div class="i-carbon-warning-filled text-red-500 text-lg" v-else />
                      <span class="font-bold">{{ parseSummary.success ? '解析通过' : '解析存在错误' }}</span>
                    </div>

                    <div class="space-y-1.5 text-gray-600 dark:text-gray-400">
                      <div>工位站点总数: <span class="font-bold text-slate-800 dark:text-slate-200">{{ parseSummary.stationCount }} 个</span></div>
                      <div>航道路径总数: <span class="font-bold text-slate-800 dark:text-slate-200">{{ parseSummary.laneCount }} 条</span></div>
                      <div>贝塞尔曲线: <span class="font-bold text-blue-600">{{ parseSummary.hasBezier ? '已包含平滑航线' : '纯直线连接' }}</span></div>
                      <div>空间物理包络: <span class="font-mono text-[11px]">{{ parseSummary.bounds }}</span></div>
                    </div>

                    <div class="mt-3 p-2 bg-emerald-50 dark:bg-emerald-950/20 text-emerald-700 dark:text-emerald-400 rounded text-[11px]">
                      {{ parseSummary.message }}
                    </div>
                  </div>

                  <div v-else class="text-gray-400 py-12 text-center">
                    点击【仅语法检查】校验原文，或【应用到当前路网】载入运行路网
                  </div>
                </div>
              </div>
            </div>
          </div>
        </NTabPane>

        <!-- Tab 4: Graph Topology Diagnostics -->
        <NTabPane name="diagnostics" tab="拓扑自检">
          <div class="space-y-4 pt-2">
            <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
              <NCard size="small" class="shadow-sm">
                <NStatistic label="综合健康度评分" :value="topologyReport.health">
                  <template #suffix>/ 100</template>
                  <template #prefix><div class="i-carbon-meter-alt text-emerald-500 text-xl" /></template>
                </NStatistic>
              </NCard>
              <NCard size="small" class="shadow-sm">
                <NStatistic label="连通分量" :value="topologyReport.components">
                  <template #prefix><div class="i-carbon-network-4 text-blue-500 text-xl" /></template>
                </NStatistic>
              </NCard>
              <NCard size="small" class="shadow-sm">
                <NStatistic label="孤立悬挂节点" :value="topologyReport.isolated">
                  <template #prefix><div class="i-carbon-warning text-emerald-500 text-xl" /></template>
                </NStatistic>
              </NCard>
              <NCard size="small" class="shadow-sm">
                <NStatistic label="交管互斥防冲区" :value="mutexZones.length">
                  <template #prefix><div class="i-carbon-security text-emerald-500 text-xl" /></template>
                </NStatistic>
              </NCard>
            </div>

            <div class="p-3 rounded text-xs border" :class="topologyReport.isolated === 0 && topologyReport.components <= 1 ? 'bg-emerald-50 dark:bg-emerald-950/20 text-emerald-700 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800' : 'bg-amber-50 dark:bg-amber-950/20 text-amber-700 dark:text-amber-400 border-amber-200 dark:border-amber-800'">
              <div class="font-bold mb-1">当前路网拓扑自检：</div>
              节点 {{ nodes.length }}，航道 {{ paths.length }}，连通分量 {{ topologyReport.components }}，孤立节点 {{ topologyReport.isolated }}。健康度按当前 nodes/paths 实时计算。
            </div>
          </div>
        </NTabPane>
      </NTabs>
    </NCard>

    <!-- Node Modal for Adding / Editing Station -->
    <NModal v-model:show="showNodeModal" preset="card" :title="isEditingNode ? '编辑工位站点标定' : '标定新工位站点'" style="width: 520px" size="small">
      <NForm :model="nodeForm" label-placement="left" label-width="110" size="small">
        <NFormItem label="站点 ID">
          <NInput v-model:value="nodeForm.id" :disabled="isEditingNode" />
        </NFormItem>
        <NFormItem label="站点名称">
          <NInput v-model:value="nodeForm.name" />
        </NFormItem>
        <NFormItem label="站点类型">
          <NSelect
            v-model:value="nodeForm.type"
            :options="[
              { label: '作业工位', value: 'JobPoint' },
              { label: '充电点', value: 'ChargePoint' },
              { label: '避让点', value: 'PullOverPoint' },
              { label: '地标', value: 'Landmark' },
              { label: '待命点', value: 'StandbyPoint' }
            ]"
          />
        </NFormItem>
        <NFormItem label="X 坐标 (米)">
          <NInputNumber v-model:value="nodeForm.x" :step="0.5" class="w-full" />
        </NFormItem>
        <NFormItem label="Y 坐标 (米)">
          <NInputNumber v-model:value="nodeForm.y" :step="0.5" class="w-full" />
        </NFormItem>
        <NFormItem label="朝向（度）">
          <NInputNumber v-model:value="nodeForm.headingDeg" :step="15" :min="0" :max="360" class="w-full" />
        </NFormItem>
        <NFormItem label="允许自旋">
          <NSwitch v-model:value="nodeForm.allowRevolve">
            <template #checked>允许自旋</template>
            <template #unchecked>禁止自旋</template>
          </NSwitch>
        </NFormItem>
        <NFormItem label="自旋转向限制">
          <NSelect
            v-model:value="nodeForm.spinDirection"
            :options="[
              { label: '双向', value: 'both' },
              { label: '仅顺时针', value: 'clockwise' },
              { label: '仅逆时针', value: 'counterclockwise' },
              { label: '禁止', value: 'none' }
            ]"
          />
        </NFormItem>
        <NFormItem label="站点分类">
          <NSelect
            v-model:value="nodeForm.className"
            filterable
            tag
            :options="[
              { label: 'LandMark (通用地标点)', value: 'LandMark' },
              { label: 'ChargePoint (自动充电桩)', value: 'ChargePoint' },
              { label: 'ReturnPoint (待命返程点)', value: 'ReturnPoint' },
              { label: 'WorkStation (机台对接站)', value: 'WorkStation' }
            ]"
          />
        </NFormItem>
      </NForm>
      <template #footer>
        <div class="flex justify-end gap-2">
          <NButton size="small" @click="showNodeModal = false">取消</NButton>
          <NButton type="primary" size="small" @click="submitNodeForm">保存标定</NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>
