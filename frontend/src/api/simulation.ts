/* eslint-disable import/no-named-as-default-member */
import axios from 'axios';
import type { SimulationTelemetry } from '@/types/fms';

const client = axios.create({
  baseURL: '/api/fms/simulation',
  timeout: 10000
});

export const simulationApi = {
  async getState(): Promise<SimulationTelemetry> {
    const res = await client.get<SimulationTelemetry>('/state');
    return res.data;
  },

  async pause(): Promise<void> {
    await client.post('/pause');
  },

  async resume(): Promise<void> {
    await client.post('/resume');
  },

  async setTimeScale(scale: number): Promise<void> {
    await client.post(`/timescale?scale=${scale}`);
  },

  async step(): Promise<void> {
    await client.post('/step');
  },

  async spawnVehicle(params: {
    id: string;
    name?: string;
    initialNodeId?: string;
    driveType?: string;
  }): Promise<void> {
    await client.post('/spawn-vehicle', params);
  },

  async injectFault(params: {
    vehicleId: string;
    faultType: 'Estop' | 'Pause' | 'LowBattery' | 'Alarm' | 'ClearFault';
  }): Promise<void> {
    await client.post('/inject-fault', params);
  },

  async runScenario(yamlContent: string): Promise<any> {
    const res = await client.post('/run-scenario', yamlContent, {
      headers: { 'Content-Type': 'application/x-yaml' }
    });
    return res.data;
  }
};
