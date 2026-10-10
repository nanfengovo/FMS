import { localStg } from '@/utils/storage';

export async function authFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const token = localStg.get('token');
  const headers = new Headers(init?.headers || {});
  if (token && !headers.has('Authorization')) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  return fetch(input, { ...init, headers });
}

export const fmsApi = {
  // 1. Simulation APIs
  async getSimulationState(): Promise<Fms.SimulationTelemetry> {
    const res = await authFetch('/api/fms/simulation/state');
    return res.json();
  },

  async pauseSimulation(): Promise<void> {
    await authFetch('/api/fms/simulation/pause', { method: 'POST' });
  },

  async resumeSimulation(): Promise<void> {
    await authFetch('/api/fms/simulation/resume', { method: 'POST' });
  },

  async setTimeScale(scale: number): Promise<void> {
    await authFetch('/api/fms/simulation/timescale', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scale })
    });
  },

  async stepSimulation(): Promise<void> {
    await authFetch('/api/fms/simulation/step', { method: 'POST' });
  },

  async spawnVehicle(params: {
    id: string;
    name?: string;
    initialNodeId?: string;
    driveType?: string;
  }): Promise<void> {
    await authFetch('/api/fms/simulation/spawn-vehicle', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(params)
    });
  },

  async getFleetVehicles(): Promise<{
    success: boolean;
    mapId?: string;
    mapName?: string;
    heartbeatTimeoutSec?: number;
    parkNodes?: Array<{ id: string; name: string; type: string }>;
    items: Fms.VehicleDto[];
  }> {
    const res = await authFetch('/api/fms/simulation/fleet/vehicles');
    const data = await res.json();
    return {
      success: Boolean(data?.success),
      mapId: data?.mapId,
      mapName: data?.mapName,
      heartbeatTimeoutSec: data?.heartbeatTimeoutSec,
      parkNodes: data?.parkNodes || [],
      items: data?.items || []
    };
  },

  async updateFleetVehicle(
    id: string,
    payload: {
      name?: string;
      vehicleType?: string;
      driveType?: string;
      length?: number;
      width?: number;
      maxSpeed?: number;
      homeNodeId?: string | null;
    }
  ): Promise<{ success: boolean; item?: Fms.VehicleDto; error?: string }> {
    const res = await authFetch(`/api/fms/simulation/fleet/vehicles/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    return res.json();
  },

  async setFleetVehicleEnabled(
    id: string,
    enabled: boolean
  ): Promise<{ success: boolean; item?: Fms.VehicleDto; error?: string }> {
    const action = enabled ? 'enable' : 'disable';
    const res = await authFetch(`/api/fms/simulation/fleet/vehicles/${encodeURIComponent(id)}/${action}`, {
      method: 'POST'
    });
    return res.json();
  },

  async injectFault(params: {
    vehicleId: string;
    faultType: 'Estop' | 'Pause' | 'LowBattery' | 'Alarm' | 'ClearFault';
  }): Promise<void> {
    await authFetch('/api/fms/simulation/inject-fault', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(params)
    });
  },

  async runScenario(yamlContent: string): Promise<any> {
    const res = await authFetch('/api/fms/simulation/run-scenario', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-yaml' },
      body: yamlContent
    });
    return res.json();
  },

  // 2. Map APIs
  async getCurrentMap(): Promise<Fms.MapGraphDto> {
    const res = await authFetch('/api/fms/map/current');
    const d = await res.json();
    if (d) {
      return {
        name: d.name || 'Map',
        bounds: d.bounds,
        totalPointCloudCount: d.totalPointCloudCount || 0,
        pointCloudCount: d.pointCloudCount || 0,
        nodes: d.nodes?.map((n: any) => ({
          id: n.id,
          name: n.name,
          x: n.x,
          y: n.y,
          type: n.type,
          headingDeg: n.headingDeg ?? 0,
          allowRevolve: n.allowRevolve ?? true,
          spinDirection: n.spinDirection ?? 'both',
          className: n.className ?? 'LandMark',
          dwellSec: n.dwellSec ?? 5
        })) || [],
        paths: d.paths?.map((p: any) => ({
          id: p.id,
          startNodeId: p.from || p.fromNodeId || p.startNodeId,
          endNodeId: p.to || p.toNodeId || p.endNodeId,
          fromNodeId: p.from || p.fromNodeId || p.startNodeId,
          toNodeId: p.to || p.toNodeId || p.endNodeId,
          pathType: (p.type === 'CubicBezier' || p.type === 'bezier' || p.curveType === 'bezier_curve') ? 'CubicBezier' : 'Straight',
          controlPoints: [
            { x: p.p1?.x ?? 0, y: p.p1?.y ?? 0 },
            { x: p.p2?.x ?? 0, y: p.p2?.y ?? 0 }
          ],
          maxSpeed: p.maxSpeed ?? 1.5,
          length: p.length ?? 0,
          isBidirectional: p.bidirectional ?? false,
          bidirectional: p.bidirectional ?? false,
          direction: p.direction ?? 'forward',
          roadWidth: p.roadWidth ?? 1.2,
          curveType: p.curveType ?? (p.type === 'bezier' ? 'bezier_curve' : 'straight_line')
        })) || [],
        mutexZones: d.zones?.map((z: any) => ({
          zoneId: z.id,
          name: z.name || z.id,
          memberNodeIds: [],
          memberPathIds: z.associatedPathIds || [],
          maxAllowedVehicles: z.maxCapacity ?? 1,
          currentOccupantVehicleIds: z.claimant ? [z.claimant] : [],
          polygon: z.polygon || [],
          timeoutSec: z.timeoutSec ?? 30,
          associatedPathIds: z.associatedPathIds || []
        })) || []
      };
    }
    return d;
  },

  async getPointCloud(limit = 25000): Promise<{ total: number; count: number; points: Fms.Point2D[] }> {
    const res = await authFetch(`/api/fms/map/point-cloud?limit=${limit}`);
    return res.json();
  },

  async getRasterMeta(): Promise<Fms.MapRasterMeta> {
    const res = await authFetch('/api/fms/map/raster/meta');
    const json = await res.json();
    return json.data || { available: false };
  },

  async getRasterBlob(): Promise<Blob> {
    const res = await authFetch('/api/fms/map/raster');
    if (!res.ok) throw new Error('底图不存在');
    return res.blob();
  },

  async uploadRaster(file: File): Promise<{ success: boolean; width: number; height: number }> {
    const formData = new FormData();
    formData.append('file', file);
    const res = await authFetch('/api/fms/map/upload-raster', { method: 'POST', body: formData });
    return res.json();
  },

  async loadKecongDemo(): Promise<{ success: boolean; mapName: string; nodeCount: number; pathCount: number; zoneCount: number; totalPoints: number; sampledPoints: number }> {
    const res = await authFetch('/api/fms/map/load-kecong-demo', { method: 'POST' });
    return res.json();
  },

  async loadBenchmark(): Promise<{ success: boolean; mapName: string; nodeCount: number; pathCount: number; zoneCount: number }> {
    const res = await authFetch('/api/fms/map/load-benchmark', { method: 'POST' });
    return res.json();
  },

  async uploadXMap(file: File): Promise<{ success: boolean; message: string; graph?: Fms.MapGraphDto }> {
    const formData = new FormData();
    formData.append('file', file);
    const res = await authFetch('/api/fms/map/upload-xmap', {
      method: 'POST',
      body: formData
    });
    return res.json();
  },

  async uploadMapinfo(file: File): Promise<{ success: boolean; message: string; graph?: Fms.MapGraphDto }> {
    const formData = new FormData();
    formData.append('file', file);
    const res = await authFetch('/api/fms/map/upload-mapinfo', {
      method: 'POST',
      body: formData
    });
    return res.json();
  },

  exportXmapUrl(): string {
    return '/api/fms/map/export-xmap';
  },

  // 3. RCS External APIs
  async getRcsHeartbeat(): Promise<{ errMsg: string; result: boolean }> {
    const res = await authFetch(`/api/xinsong/heart_beat?timestamp=${Date.now()}`);
    return res.json();
  },

  async addRcsTask(req: Fms.RcsTaskAddRequest): Promise<{ errMsg?: string; result: boolean }> {
    const res = await authFetch('/api/v1/xinsong/task_add', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });
    return res.json();
  },

  async deleteRcsTask(taskId: string): Promise<{ errMsg?: string; result: boolean }> {
    const res = await authFetch('/api/v1/xinsong/task_delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ task_serial: taskId, delete_task_count: 1 })
    });
    return res.json();
  },

  // 4. Logging & Flight Recorder
  async getAuditLogs(limit = 100, actionType?: string): Promise<Fms.AuditLogEntry[]> {
    const params = new URLSearchParams();
    params.set('pageSize', String(limit));
    if (actionType) params.set('action', actionType);
    const res = await authFetch(`/api/fms/logs/audit?${params.toString()}`);
    const data = await res.json();
    if (data?.data?.items) {
      return data.data.items.map((i: any) => ({
        id: i.id,
        timestamp: i.timestamp,
        operator: i.operator,
        actionType: i.action,
        entityType: i.resource,
        entityId: i.resource,
        details: i.details,
        success: i.success
      }));
    }
    return [];
  },

  async getFlightRecorderRecent(seconds = 30): Promise<Fms.FlightRecorderFrame[]> {
    const limit = seconds * 20;
    const res = await authFetch(`/api/fms/logs/flight-recorder/recent?limit=${limit}`);
    const data = await res.json();
    return this.convertSnapshotsToFrames(data?.data?.frames || []);
  },

  async getFlightRecorderRange(fromMs: number, toMs: number): Promise<Fms.FlightRecorderFrame[]> {
    const res = await authFetch(`/api/fms/logs/flight-recorder/range?startMs=${fromMs}&endMs=${toMs}`);
    const data = await res.json();
    return this.convertSnapshotsToFrames(data?.data?.frames || []);
  },

  convertSnapshotsToFrames(rawFrames: any[]): Fms.FlightRecorderFrame[] {
    if (!rawFrames || rawFrames.length === 0) return [];
    const map = new Map<number, any[]>();
    for (const f of rawFrames) {
      const t = f.timestampMs ?? f.virtualTimeMs ?? 0;
      if (!map.has(t)) {
        map.set(t, []);
      }
      map.get(t)!.push(f);
    }
    const sortedTimes = Array.from(map.keys()).sort((a, b) => a - b);
    return sortedTimes.map((timeMs, idx) => {
      const snaps = map.get(timeMs) || [];
      return {
        tickIndex: idx + 1,
        virtualTimeMs: timeMs,
        vehicles: snaps.map(s => {
          const rawStatus = typeof s.status === 'string' ? s.status : (s.status === 1 ? 'Running' : s.status === 0 ? 'Idle' : s.status === 4 ? 'Error' : 'Running');
          const poseObj = s.pose || { x: s.x ?? 0, y: s.y ?? 0, thetaDeg: s.theta ?? 0 };
          return {
            id: s.vehicleId || s.id || 'AGV',
            name: s.vehicleId || s.id || 'AGV',
            status: (rawStatus as any) || 'Idle',
            pose: {
              x: poseObj.x ?? 0,
              y: poseObj.y ?? 0,
              theta: poseObj.thetaDeg ?? poseObj.theta ?? 0
            },
            currentSpeed: s.speed ?? s.currentSpeed ?? 0,
            batteryPercent: s.batteryPercent ?? 100,
            batteryVoltage: 48.0,
            currentNodeId: s.currentNodeId || undefined,
            targetNodeId: s.targetNodeId || undefined,
            currentOrderId: s.assignedOrderId || s.orderId || undefined,
            holdingPayload: false,
            driveType: 'Differential'
          };
        }),
        orders: [],
        mutexZones: [],
        conflictAlerts: []
      };
    });
  },

  async clearFlightRecorder(): Promise<void> {
    await authFetch('/api/fms/logs/flight-recorder/clear', { method: 'POST' });
  },

  // 5. Configuration & Hot Reload
  async getConfigBundle(): Promise<Fms.FmsConfigBundle> {
    const res = await authFetch('/api/fms/config');
    const data = await res.json();
    const d = data?.data || {};
    return {
      customSet: JSON.stringify(d.customSet || {}, null, 2),
      kcfmsXml: JSON.stringify(d.kcFmsXml || {}, null, 2),
      controlTags: Array.isArray(d.controlTags) ? d.controlTags.reduce((acc: any, cur: string) => ({ ...acc, [cur]: '1' }), {}) : (d.controlTags || {}),
      trafficConfig: {
        maxSpeedLimit: d.traffic?.maxSpeedLimit ?? 1.5,
        defaultTurningRadius: 1.0,
        deadlockTimeoutSec: d.traffic?.deadlockTimeoutSec ?? 15,
        collisionSafetyRadiusMeters: d.traffic?.corridorSafetyBuffer ?? 0.8,
        highDensityThreshold: 4,
        priorityWeights: {},
        emptyYieldLoaded: d.traffic?.emptyYieldLoaded ?? true,
        defaultAvoidanceStrategy: d.traffic?.defaultAvoidanceStrategy ?? 2,
        corridorSafetyBuffer: d.traffic?.corridorSafetyBuffer ?? 0.8
      },
      pushStats: {
        totalPushed: d.pushStats?.totalPushed ?? 0,
        lastPushedAt: d.pushStats?.lastPushedAt ?? null
      }
    };
  },

  async getRcsProfiles(): Promise<{
    activeProfile: Fms.RcsProfileDto;
    profiles: Fms.RcsProfileDto[];
  }> {
    const res = await authFetch('/api/fms/config/profiles');
    const data = await res.json();
    return data?.data || { activeProfile: null, profiles: [] };
  },

  async setActiveRcsProfile(profileId: string): Promise<{ success: boolean; data?: Fms.RcsProfileDto; msg?: string }> {
    const res = await authFetch('/api/fms/config/profiles/active', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ profileId })
    });
    const data = await res.json();
    return { success: data?.code === '0000', data: data?.data, msg: data?.msg };
  },

  async saveRcsProfile(profile: Fms.RcsProfileDto): Promise<{ success: boolean; data?: Fms.RcsProfileDto; msg?: string }> {
    const res = await authFetch('/api/fms/config/profiles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(profile)
    });
    const data = await res.json();
    return { success: data?.code === '0000', data: data?.data, msg: data?.msg };
  },

  async testEndpoint(payload: { key: string; path: string; timeoutMs?: number }): Promise<{
    success: boolean;
    status: number;
    rttMs: number;
    url?: string;
    body?: string;
    error?: string;
  }> {
    const res = await authFetch('/api/fms/config/test-endpoint', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    return res.json();
  },

  async applyEditorMapJson(jsonContent: string): Promise<{
    success: boolean;
    mapName?: string;
    nodeCount?: number;
    pathCount?: number;
    zoneCount?: number;
    message?: string;
    error?: string;
  }> {
    const res = await authFetch('/api/fms/map/apply-json', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jsonContent })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      return { success: false, error: data.error || data.msg || data.message || '应用到当前路网失败' };
    }
    return data;
  },

  async updateCustomSet(content: string, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    let payload: any = { ip: '127.0.0.1', port: 8088, urls: {} };
    try {
      payload = JSON.parse(content);
    } catch {
      payload = { ip: '127.0.0.1', port: 8088, urls: {} };
    }
    const res = await authFetch(`/api/fms/config/custom-set?operator=${encodeURIComponent(operator)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async updateKcfmsXml(content: string, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    let payload: any = { server: '127.0.0.1', port: 8088, enable: true, url: '/api/v1/xinsong/report_robot_status' };
    try {
      payload = JSON.parse(content);
    } catch {
      payload = { server: '127.0.0.1', port: 8088, enable: true, url: '/api/v1/xinsong/report_robot_status' };
    }
    const res = await authFetch(`/api/fms/config/kcfms-xml?operator=${encodeURIComponent(operator)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async updateControlTags(tags: Record<string, string>, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    const list = Object.keys(tags);
    const res = await authFetch(`/api/fms/config/control-tags?operator=${encodeURIComponent(operator)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(list)
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async updateTrafficConfig(config: Fms.TrafficConfig, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/config/traffic?operator=${encodeURIComponent(operator)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        deadlockTimeoutSec: config.deadlockTimeoutSec,
        defaultAvoidanceStrategy: config.defaultAvoidanceStrategy ?? 2,
        batteryLowThreshold: 0.20,
        maxSpeedLimit: config.maxSpeedLimit,
        corridorSafetyBuffer: config.corridorSafetyBuffer ?? config.collisionSafetyRadiusMeters,
        emptyYieldLoaded: config.emptyYieldLoaded ?? true
      })
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  // 6. Access control
  async getAccessCatalog(): Promise<Fms.PermissionNode[]> {
    const res = await authFetch('/api/system/access/catalog');
    const data = await res.json();
    return data?.code === '0000' ? data.data || [] : [];
  },

  async getAccessRoles(): Promise<Fms.AccessRole[]> {
    const res = await authFetch('/api/system/access/roles');
    const data = await res.json();
    return data?.code === '0000' ? data.data || [] : [];
  },

  async replaceRoleGrants(roleName: string, permissions: string[]): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/system/access/roles/${encodeURIComponent(roleName)}/grants`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ permissions })
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async getAccessUsers(): Promise<Fms.AccessUser[]> {
    const res = await authFetch('/api/system/access/users');
    const data = await res.json();
    return data?.code === '0000' ? data.data || [] : [];
  },

  async replaceUserRoles(userId: string, roles: string[]): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/system/access/users/${encodeURIComponent(userId)}/roles`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ roles })
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async getRoles(): Promise<Fms.RoleDto[]> {
    const roles = await this.getAccessRoles();
    return roles.map(role => ({
      name: role.roleName,
      description: role.description,
      permissions: role.permissions
    }));
  },

  // 7. Map Workbench & Mutex Zone Force Release
  async forceReleaseMutexZone(zoneId: string, operator = 'Admin', reason = 'Manual emergency clear'): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/map/zones/${zoneId}/force-release?operator=${encodeURIComponent(operator)}&reason=${encodeURIComponent(reason)}`, {
      method: 'POST'
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async deleteMapNode(id: string, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/map/nodes/${id}?operator=${encodeURIComponent(operator)}`, {
      method: 'DELETE'
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  async deleteMapPath(id: string, operator = 'Admin'): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/map/paths/${id}?operator=${encodeURIComponent(operator)}`, {
      method: 'DELETE'
    });
    const data = await res.json();
    return { success: data.code === '0000', message: data.msg };
  },

  // 8. Workflow Management & Live Execution
  async getWorkflowTemplates(): Promise<any[]> {
    const res = await authFetch('/api/fms/workflows');
    const data = await res.json();
    return data?.data || [];
  },

  async getWorkflowTemplate(id: string): Promise<any> {
    const res = await authFetch(`/api/fms/workflows/${id}`);
    const data = await res.json();
    return data?.data;
  },

  async getWorkflowHistory(id: string): Promise<any[]> {
    const res = await authFetch(`/api/fms/workflows/${id}/history`);
    const data = await res.json();
    return data?.data || [];
  },

  async saveWorkflowTemplate(template: any): Promise<{ success: boolean; message: string; data?: any }> {
    const res = await authFetch('/api/fms/workflows', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(template)
    });
    return res.json();
  },

  async deleteWorkflowTemplate(id: string): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/workflows/${id}`, {
      method: 'DELETE'
    });
    return res.json();
  },

  async runWorkflow(params: {
    templateId?: string;
    taskSerial?: string;
    vehicleId?: string;
    priority?: number;
    steps?: any[];
  }): Promise<{ success: boolean; orderId?: string; orderSerial?: string; error?: string }> {
    const res = await authFetch('/api/fms/workflows/run', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(params)
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      return { success: false, error: data.error || data.msg || data.message || '下发失败' };
    }
    return data;
  },

  async listReplayScenes(): Promise<{ success: boolean; items: Array<{ id: string; name: string; mapName: string; traceId: string; vehicleId: string; status: string; startSimMs: number; endSimMs: number; sampleCount: number; createdAt: string }> }> {
    const res = await authFetch('/api/fms/replay');
    return res.json();
  },

  async getReplayScene(id: string): Promise<any> {
    const res = await authFetch(`/api/fms/replay/${encodeURIComponent(id)}`);
    return res.json();
  },

  async captureReplay(traceId?: string, seconds = 45): Promise<{ success: boolean; id: string; name: string; sampleCount: number }> {
    const res = await authFetch('/api/fms/replay/capture', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ traceId, seconds })
    });
    return res.json();
  },

  async listLiveWorkflows(): Promise<{ success: boolean; items: Array<{ instanceId: string; name: string; status: string; vehicleId?: string; currentStep?: string; targetNodeId?: string; startedAt?: string }> }> {
    const res = await authFetch('/api/fms/workflows/live');
    return res.json();
  },

  async getWorkflowExecution(orderId: string): Promise<any> {
    const res = await authFetch(`/api/fms/workflows/execution/${orderId}`);
    return res.json();
  },

  async controlWorkflowExecution(orderId: string, command: 'skip' | 'cancel', reason?: string): Promise<{ success: boolean; message?: string; error?: string }> {
    const res = await authFetch(`/api/fms/workflows/execution/${orderId}/control`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ command, reason })
    });
    return res.json();
  },

  // 6. Virtual PLC & IO Signals
  async getIoSignals(): Promise<{ success: boolean; signals: Record<string, string> }> {
    const res = await authFetch('/api/fms/simulation/io-signals');
    return res.json();
  },

  async setIoSignal(signalName: string, value: string): Promise<{ success: boolean; signalName: string; value: string }> {
    const res = await authFetch('/api/fms/simulation/io-signals', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ signalName, value })
    });
    return res.json();
  },

  async toggleIoSignal(signalName: string): Promise<{ success: boolean; signalName: string; value: string }> {
    const res = await authFetch('/api/fms/simulation/io-signals/toggle', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ signalName })
    });
    return res.json();
  },

  // 7. Industrial Protocol Gateway (Modbus-TCP / MQTT)
  async getGatewayChannels(): Promise<{ success: boolean; data: any[] }> {
    const res = await authFetch('/api/fms/gateway/channels');
    return res.json();
  },

  async saveGatewayChannel(dto: any): Promise<{ success: boolean; data: any }> {
    const res = await authFetch('/api/fms/gateway/channels', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto)
    });
    return res.json();
  },

  async connectGatewayChannel(id: string): Promise<{ success: boolean; status: string; latencyMs: number; error?: string }> {
    const res = await authFetch(`/api/fms/gateway/channels/${id}/connect`, { method: 'POST' });
    return res.json();
  },

  async disconnectGatewayChannel(id: string): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/gateway/channels/${id}/disconnect`, { method: 'POST' });
    return res.json();
  },

  async writeGatewayTag(channelId: string, tagAddress: string, value: string): Promise<{ success: boolean; tagAddress: string; value: string }> {
    const res = await authFetch('/api/fms/gateway/tags/write', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ channelId, tagAddress, value })
    });
    return res.json();
  },

  async getSimulatedModbusStatus(): Promise<{ isRunning: boolean; port: number; dockReadyCoil100: boolean; lineClearCoil101: boolean; materialArrivedCoil102: boolean }> {
    const res = await authFetch('/api/fms/gateway/simulated-server/status');
    return res.json();
  },

  async setSimulatedModbusCoil(address: number, value: boolean): Promise<{ success: boolean; address: number; value: boolean }> {
    const res = await authFetch('/api/fms/gateway/simulated-server/set-coil', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ address, value })
    });
    return res.json();
  },

  // 8. Fleet Benchmark & Performance Profiling
  async getBenchmarkStats(): Promise<{ success: boolean; data: any }> {
    const res = await authFetch('/api/fms/simulation/benchmark/stats');
    return res.json();
  },

  async runFleetStressTest(vehicleCount = 30, simulatedTicks = 100): Promise<{ success: boolean; data: any }> {
    const res = await authFetch('/api/fms/simulation/benchmark/stress-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ vehicleCount, simulatedTicks })
    });
    return res.json();
  },

  // 9. Dynamic Closure & Fault Recovery Matrix
  async setPathClosure(pathId: string, isClosed: boolean, reason?: string): Promise<{ success: boolean; pathId: string; isClosed: boolean }> {
    const res = await authFetch('/api/fms/simulation/closure/path', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pathId, isClosed, reason })
    });
    return res.json();
  },

  async setNodeClosure(nodeId: string, isClosed: boolean, reason?: string): Promise<{ success: boolean; nodeId: string; isClosed: boolean }> {
    const res = await authFetch('/api/fms/simulation/closure/node', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ nodeId, isClosed, reason })
    });
    return res.json();
  },

  async retryOrderStep(orderId: string): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/simulation/orders/${orderId}/retry`, { method: 'POST' });
    return res.json();
  },

  async rollbackOrder(orderId: string, fallbackStationId?: string): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/fms/simulation/orders/${orderId}/rollback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fallbackStationId })
    });
    return res.json();
  },

  // 10. Storage Zones & Slots
  async getStorageZones(): Promise<any[]> {
    const res = await authFetch('/api/fms/storage/zones');
    const json = await res.json();
    return json.data || [];
  },

  async getStorageSlots(zoneId?: string, status?: string): Promise<any[]> {
    let url = '/api/fms/storage/slots';
    const params = new URLSearchParams();
    if (zoneId) params.append('zoneId', zoneId);
    if (status) params.append('status', status);
    if (params.toString()) url += `?${params.toString()}`;
    const res = await authFetch(url);
    const json = await res.json();
    return json.data || [];
  },

  async updateSlotStatus(id: string, status: string, carrierId?: string, lockReason?: string): Promise<any> {
    const res = await authFetch(`/api/fms/storage/slots/${encodeURIComponent(id)}/status`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status, carrierId, lockReason })
    });
    return res.json();
  },

  async lockSlot(id: string, reason?: string): Promise<any> {
    const res = await authFetch(`/api/fms/storage/slots/${encodeURIComponent(id)}/lock`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason })
    });
    return res.json();
  },

  async unlockSlot(id: string): Promise<any> {
    const res = await authFetch(`/api/fms/storage/slots/${encodeURIComponent(id)}/unlock`, {
      method: 'POST'
    });
    return res.json();
  },

  // 11. Decision Logs (Track 2)
  async getDecisionLogs(category?: string, subjectId?: string, limit = 100): Promise<any> {
    let url = `/api/fms/logs/decisions?limit=${limit}`;
    if (category) url += `&category=${encodeURIComponent(category)}`;
    if (subjectId) url += `&subjectId=${encodeURIComponent(subjectId)}`;
    const res = await authFetch(url);
    const json = await res.json();
    return json.data || { items: [], total: 0 };
  },

  // 12. Road Network & Lines
  async publishRoadNetwork(): Promise<any> {
    const res = await authFetch('/api/fms/map/publish', { method: 'POST' });
    return res.json();
  },

  async getRouteLines(): Promise<any[]> {
    const res = await authFetch('/api/fms/map/lines');
    const json = await res.json();
    return json.data || [];
  },

  async saveRouteLine(line: any): Promise<any> {
    const res = await authFetch('/api/fms/map/lines', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(line)
    });
    return res.json();
  },

  async queryLogs(params: Fms.LogQueryParams): Promise<Fms.LogQueryResult> {
    const search = new URLSearchParams();
    if (params.category) search.set('category', params.category);
    if (params.source) search.set('source', params.source);
    if (params.keyword) search.set('keyword', params.keyword);
    if (params.level) search.set('level', params.level);
    if (params.from) search.set('from', params.from);
    if (params.to) search.set('to', params.to);
    if (params.taskSerial) search.set('taskSerial', params.taskSerial);
    if (params.vehicleId) search.set('vehicleId', params.vehicleId);
    search.set('page', String(params.page || 1));
    search.set('pageSize', String(params.pageSize || 50));
    const res = await authFetch(`/api/logs?${search.toString()}`);
    const json = await res.json();
    return json.data || { items: [], total: 0, lines: [] };
  },

  async getLogWiki(): Promise<any[]> {
    const res = await authFetch('/api/logs/wiki');
    const json = await res.json();
    return json.data || [];
  },

  async saveLogWiki(id: string, title: string, body: string): Promise<{ success: boolean; message: string }> {
    const res = await authFetch(`/api/logs/wiki/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, body })
    });
    const json = await res.json();
    return { success: json.code === '0000', message: json.msg };
  },

  async getLogSettings(): Promise<any> {
    const res = await authFetch('/api/logs/settings');
    const json = await res.json();
    return json.data;
  },

  async saveLogSettings(payload: Record<string, string>): Promise<{ success: boolean; message: string }> {
    const res = await authFetch('/api/logs/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const json = await res.json();
    return { success: json.code === '0000', message: json.msg };
  },

  async testLogSettings(payload?: Record<string, string>): Promise<{ success: boolean; message: string; latencyMs: number; reply?: string }> {
    const res = await authFetch('/api/logs/settings/test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: payload ? JSON.stringify(payload) : undefined
    });
    const json = await res.json();
    return json.data || { success: false, message: '请求失败', latencyMs: 0 };
  },

  async listLogModels(payload?: Record<string, string>): Promise<{ success: boolean; message: string; models: Array<{ id: string; displayName: string; description: string; recommended: boolean }> }> {
    const res = await authFetch('/api/logs/settings/models', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: payload ? JSON.stringify(payload) : undefined
    });
    const json = await res.json();
    return json.data || { success: false, message: '获取失败', models: [] };
  },

  async analyzeLogs(question: string, mode: string): Promise<any> {
    const res = await authFetch('/api/logs/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, mode })
    });
    const json = await res.json();
    return json.data;
  }
};


