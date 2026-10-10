import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { fmsApi } from '@/service/api/fms';

export const useMapStore = defineStore('map', () => {
  const currentGraph = ref<Fms.MapGraphDto | null>(null);
  const pointCloud = ref<Fms.Point2D[]>([]);
  const totalPointCloudCount = ref(0);
  const raster = ref<{ url: string; originX: number; topY: number; widthMeters: number; heightMeters: number; sourceName?: string } | null>(null);
  let rasterObjectUrl = '';
  const isLoading = ref(false);
  const selectedNodeId = ref<string | null>(null);
  const selectedPathId = ref<string | null>(null);

  const layerVisibility = ref({
    nodes: true,
    straightPaths: true,
    bezierPaths: true,
    arrows: true,
    mutexZones: true,
    lidarPoints: false,
    raster: true,
    stationLabels: true
  });

  const selectedNode = computed<Fms.MapNodeDto | null>(() => {
    if (!currentGraph.value || !selectedNodeId.value) return null;
    return currentGraph.value.nodes.find(n => n.id === selectedNodeId.value) || null;
  });

  const selectedPath = computed<Fms.MapPathDto | null>(() => {
    if (!currentGraph.value || !selectedPathId.value) return null;
    return currentGraph.value.paths.find(p => p.id === selectedPathId.value) || null;
  });

  const stats = computed(() => {
    if (!currentGraph.value) {
      return { totalNodes: 0, totalPaths: 0, bezierPaths: 0, totalLength: 0, zones: 0, pointCloud: 0 };
    }
    const nodes = currentGraph.value.nodes.length;
    const paths = currentGraph.value.paths.length;
    const bezierPaths = currentGraph.value.paths.filter(p => p.pathType === 'CubicBezier').length;
    const totalLength = currentGraph.value.paths.reduce((acc, p) => acc + (p.length || 0), 0);
    const zones = currentGraph.value.mutexZones?.length || 0;
    return {
      totalNodes: nodes,
      totalPaths: paths,
      bezierPaths,
      totalLength: Math.round(totalLength * 10) / 10,
      zones,
      pointCloud: totalPointCloudCount.value
    };
  });

  async function loadCurrentMap() {
    isLoading.value = true;
    try {
      const graph = await fmsApi.getCurrentMap();
      currentGraph.value = graph;
      totalPointCloudCount.value = graph.totalPointCloudCount || 0;
      if (totalPointCloudCount.value > 0 && pointCloud.value.length === 0) {
        await loadPointCloud();
      }
      await loadRaster();
    } catch (e) {
      console.error('Failed to load current map:', e);
    } finally {
      isLoading.value = false;
    }
  }

  async function loadRaster() {
    try {
      const meta = await fmsApi.getRasterMeta();
      if (!meta.available || meta.originX === undefined || meta.topY === undefined || !meta.widthMeters || !meta.heightMeters) {
        if (rasterObjectUrl) URL.revokeObjectURL(rasterObjectUrl);
        rasterObjectUrl = '';
        raster.value = null;
        return;
      }
      const blob = await fmsApi.getRasterBlob();
      if (rasterObjectUrl) URL.revokeObjectURL(rasterObjectUrl);
      rasterObjectUrl = URL.createObjectURL(blob);
      raster.value = {
        url: rasterObjectUrl,
        originX: meta.originX,
        topY: meta.topY,
        widthMeters: meta.widthMeters,
        heightMeters: meta.heightMeters,
        sourceName: meta.sourceName
      };
    } catch {
      raster.value = null;
    }
  }

  async function uploadRaster(file: File) {
    const result = await fmsApi.uploadRaster(file);
    await loadRaster();
    layerVisibility.value.raster = true;
    return result;
  }

  async function loadPointCloud(limit = 25000) {
    try {
      const res = await fmsApi.getPointCloud(limit);
      pointCloud.value = res.points || [];
      totalPointCloudCount.value = res.total || 0;
    } catch (e) {
      console.error('Failed to load point cloud:', e);
    }
  }

  async function loadKecongDemo() {
    isLoading.value = true;
    try {
      const res = await fmsApi.loadKecongDemo();
      await loadCurrentMap();
      await loadPointCloud();
      layerVisibility.value.lidarPoints = true;
      return res;
    } finally {
      isLoading.value = false;
    }
  }

  async function loadBenchmark() {
    isLoading.value = true;
    try {
      const res = await fmsApi.loadBenchmark();
      await loadCurrentMap();
      pointCloud.value = [];
      totalPointCloudCount.value = 0;
      raster.value = null;
      layerVisibility.value.lidarPoints = false;
      return res;
    } finally {
      isLoading.value = false;
    }
  }

  async function uploadXMap(file: File) {
    isLoading.value = true;
    try {
      const res = await fmsApi.uploadXMap(file);
      await loadCurrentMap();
      await loadPointCloud();
      return res;
    } finally {
      isLoading.value = false;
    }
  }

  async function uploadMapinfo(file: File) {
    isLoading.value = true;
    try {
      const res = await fmsApi.uploadMapinfo(file);
      await loadCurrentMap();
      return res;
    } finally {
      isLoading.value = false;
    }
  }

  return {
    currentGraph,
    pointCloud,
    totalPointCloudCount,
    raster,
    isLoading,
    selectedNodeId,
    selectedPathId,
    selectedNode,
    selectedPath,
    layerVisibility,
    stats,
    loadCurrentMap,
    loadPointCloud,
    uploadRaster,
    loadKecongDemo,
    loadBenchmark,
    uploadXMap,
    uploadMapinfo
  };
});
