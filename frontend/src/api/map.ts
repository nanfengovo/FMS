/* eslint-disable import/no-named-as-default-member */
import axios from 'axios';
import type { MapGraphDto } from '@/types/fms';

const client = axios.create({
  baseURL: '/api/fms/map',
  timeout: 30000
});

export const mapApi = {
  async getCurrentMap(): Promise<MapGraphDto> {
    const res = await client.get<MapGraphDto>('/current');
    return res.data;
  },

  async uploadXMap(file: File): Promise<{ success: boolean; message: string; graph?: MapGraphDto }> {
    const formData = new FormData();
    formData.append('file', file);
    const res = await client.post('/upload-xmap', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return res.data;
  },

  async uploadMapinfo(file: File): Promise<{ success: boolean; message: string; graph?: MapGraphDto }> {
    const formData = new FormData();
    formData.append('file', file);
    const res = await client.post('/upload-mapinfo', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return res.data;
  }
};
