import { ref } from 'vue';
import * as signalR from '@microsoft/signalr';
import { localStg } from '@/utils/storage';

type TelemetryCallback = (telemetry: Fms.SimulationTelemetry) => void;
type ConflictCallback = (conflict: Fms.ConflictAlertDto) => void;
type OrderSnapshotCallback = (orders: any[]) => void;

class FmsSignalRService {
  private connection: signalR.HubConnection | null = null;
  private isStarted = false;
  private telemetryListeners: TelemetryCallback[] = [];
  private conflictListeners: ConflictCallback[] = [];
  private orderListeners: OrderSnapshotCallback[] = [];
  public isConnected = ref(false);
  public lastError = ref<string | null>(null);

  public init() {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/fms', {
        accessTokenFactory: () => localStg.get('token') || '',
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('ReceiveTelemetry', (telemetry: Fms.SimulationTelemetry) => {
      for (const listener of this.telemetryListeners) {
        try {
          listener(telemetry);
        } catch (e) {
          console.error('Error in telemetry listener:', e);
        }
      }
    });

    this.connection.on('ReceiveTelemetryBatch', (batch: any[]) => {
      const telemetry: Fms.SimulationTelemetry = {
        virtualTimeSeconds: Date.now() / 1000.0,
        tickIndex: 0,
        timeScale: 1.0,
        isPaused: false,
        vehicles: batch.map(v => ({
          id: v.id,
          name: v.name || v.id,
          code: v.code,
          vehicleType: v.vehicleType,
          status: v.status,
          pose: v.pose
            ? {
                x: v.pose.x ?? v.x ?? 0,
                y: v.pose.y ?? v.y ?? 0,
                theta: v.pose.theta ?? 0
              }
            : { x: v.x ?? 0, y: v.y ?? 0, theta: ((v.theta ?? 0) * Math.PI) / 180 },
          currentSpeed: v.currentSpeed ?? v.speed,
          batteryPercent: v.batteryPercent ?? v.battery,
          batteryVoltage: v.batteryVoltage ?? 48.0,
          isCharging: v.isCharging,
          lowBatteryThreshold: v.lowBatteryThreshold,
          length: v.length,
          width: v.width,
          maxSpeed: v.maxSpeed,
          currentNodeId: v.currentNodeId ?? v.nodeId ?? null,
          nearestNodeId: v.nearestNodeId ?? v.currentNodeId ?? null,
          targetNodeId: v.targetNodeId ?? null,
          homeNodeId: v.homeNodeId ?? null,
          currentOrderId: v.assignedOrderId ?? v.currentOrderId ?? v.orderId ?? null,
          holdingPayload: Boolean(v.holdingPayload ?? v.payload),
          driveType: v.driveType ?? 'Differential',
          avoidance: v.avoidance,
          blockedBy: v.blockedBy ?? null,
          isEnabled: v.isEnabled,
          isOnline: v.isOnline,
          presenceOnline: v.presenceOnline,
          offlineReason: v.offlineReason,
          lastHeartbeatUtc: v.lastHeartbeatUtc ?? v.lastTelemetryUtc,
          lastTelemetryUtc: v.lastTelemetryUtc ?? v.lastHeartbeatUtc,
          heartbeatTimeoutSec: v.heartbeatTimeoutSec,
          mapId: v.mapId,
          mapName: v.mapName,
          dispatchEligible: v.dispatchEligible
        }))
      };
      for (const listener of this.telemetryListeners) {
        try {
          listener(telemetry);
        } catch (e) {
          console.error('Error in telemetry batch listener:', e);
        }
      }
    });

    this.connection.on('ReceiveOrderSnapshot', (orders: any[]) => {
      for (const listener of this.orderListeners) {
        try {
          listener(orders);
        } catch (e) {
          console.error('Error in order snapshot listener:', e);
        }
      }
    });

    this.connection.on('ReceiveDeadlockAlert', (conflict: Fms.ConflictAlertDto) => {
      for (const listener of this.conflictListeners) {
        try {
          listener(conflict);
        } catch (e) {
          console.error('Error in conflict listener:', e);
        }
      }
    });

    this.connection.on('ReceiveConflictAlert', (alert: any) => {
      const conflictDto: Fms.ConflictAlertDto = {
        timestamp: new Date().toLocaleTimeString(),
        vehicle1Id: alert.vehicle1Id || alert.id || 'AMR',
        vehicle2Id: alert.vehicle2Id || 'AMR',
        conflictType: alert.conflictType || 'HeadOnDeadlock',
        conflictLocation: alert.location || 'Narrow Aisle',
        resolution: typeof alert === 'string' ? alert : alert.resolution || '拉入支线避让'
      };
      for (const listener of this.conflictListeners) {
        try {
          listener(conflictDto);
        } catch (e) {
          console.error('Error in conflict alert listener:', e);
        }
      }
    });

    this.connection.onreconnecting(() => {
      this.isConnected.value = false;
    });

    this.connection.onreconnected(() => {
      this.isConnected.value = true;
      this.lastError.value = null;
    });

    this.connection.onclose(() => {
      this.isConnected.value = false;
    });
  }

  public async start() {
    this.init();
    if (!this.connection || this.isStarted) return;

    try {
      await this.connection.start();
      this.isStarted = true;
      this.isConnected.value = true;
      this.lastError.value = null;
    } catch (err: any) {
      this.isConnected.value = false;
      this.lastError.value = err?.message || 'Connection failed';
      setTimeout(() => {
        this.isStarted = false;
        this.start();
      }, 3000);
    }
  }

  public async stop() {
    if (this.connection && this.isStarted) {
      await this.connection.stop();
      this.isStarted = false;
      this.isConnected.value = false;
    }
  }

  public onTelemetry(callback: TelemetryCallback) {
    this.telemetryListeners.push(callback);
    return () => {
      const idx = this.telemetryListeners.indexOf(callback);
      if (idx >= 0) this.telemetryListeners.splice(idx, 1);
    };
  }

  public onOrders(callback: OrderSnapshotCallback) {
    this.orderListeners.push(callback);
    return () => {
      const idx = this.orderListeners.indexOf(callback);
      if (idx >= 0) this.orderListeners.splice(idx, 1);
    };
  }

  public onConflict(callback: ConflictCallback) {
    this.conflictListeners.push(callback);
    return () => {
      const idx = this.conflictListeners.indexOf(callback);
      if (idx >= 0) this.conflictListeners.splice(idx, 1);
    };
  }
}

export const signalRService = new FmsSignalRService();
