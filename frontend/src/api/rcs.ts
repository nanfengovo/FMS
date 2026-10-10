/* eslint-disable import/no-named-as-default-member */
import axios from 'axios';
import type { RcsTaskAddRequest } from '@/types/fms';

const client = axios.create({
  timeout: 10000
});

export const rcsApi = {
  async getHeartbeat(): Promise<{ errMsg: string; result: boolean }> {
    const timestamp = Date.now();
    const res = await client.get<{ errMsg: string; result: boolean }>(
      `/api/xinsong/heart_beat?timestamp=${timestamp}`
    );
    return res.data;
  },

  async addTask(req: RcsTaskAddRequest): Promise<{ code: number; message: string; reqCode?: string }> {
    const res = await client.post<{ code: number; message: string; reqCode?: string }>(
      '/api/v1/xinsong/task_add',
      req
    );
    return res.data;
  },

  async deleteTask(taskId: string): Promise<{ code: number; message: string; reqCode?: string }> {
    const res = await client.post<{ code: number; message: string; reqCode?: string }>(
      '/api/v1/xinsong/task_delete',
      { task_id: taskId }
    );
    return res.data;
  }
};
