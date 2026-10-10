<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { fmsApi } from '@/service/api/fms';
import { actorLabel, auditActionLabel, formatTimestamp } from '@/utils/fms-display';

const actionOptions = [
  { label: '全部动作', value: '' },
  { label: '登录成功 USER_LOGIN_SUCCESS', value: 'USER_LOGIN_SUCCESS' },
  { label: '登录失败 USER_LOGIN_FAILED', value: 'USER_LOGIN_FAILED' },
  { label: '订单启动 ORDER_STARTED', value: 'ORDER_STARTED' },
  { label: '订单完成 ORDER_COMPLETED', value: 'ORDER_COMPLETED' },
  { label: '订单提交 ORDER_SUBMITTED', value: 'ORDER_SUBMITTED' },
  { label: '订单取消 ORDER_CANCELLED', value: 'ORDER_CANCELLED' },
  { label: '配置更新 UPDATE_CONFIG', value: 'UPDATE_CONFIG' },
  { label: '发布路网 PUBLISH_ROAD_NETWORK', value: 'PUBLISH_ROAD_NETWORK' },
  { label: '强制释放互斥 FORCE_RELEASE_MUTEX', value: 'FORCE_RELEASE_MUTEX' },
  { label: '保存站点 SAVE_NODE', value: 'SAVE_NODE' },
  { label: '保存航道 SAVE_PATH', value: 'SAVE_PATH' }
];

const selectedAction = ref('');
const logs = ref<Fms.AuditLogEntry[]>([]);
const loading = ref(false);

async function fetchLogs() {
  loading.value = true;
  try {
    logs.value = await fmsApi.getAuditLogs(80, selectedAction.value || undefined);
  } catch {
    logs.value = [];
  } finally {
    loading.value = false;
  }
}

watch(selectedAction, fetchLogs);
onMounted(fetchLogs);
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-4 p-4 pb-12">
    <NCard title="安全审计" size="medium" class="shadow-sm">
      <template #header-extra>
        <NSpace align="center" :size="8">
          <NSelect
            v-model:value="selectedAction"
            size="small"
            style="width: 280px"
            :options="actionOptions"
          />
          <NButton size="small" :loading="loading" @click="fetchLogs">刷新</NButton>
        </NSpace>
      </template>

      <div v-if="loading && logs.length === 0" class="py-16 text-center text-xs text-slate-400">正在加载审计记录…</div>
      <div v-else-if="logs.length === 0" class="py-16 text-center text-xs text-slate-400">暂无审计事件</div>
      <div v-else class="overflow-x-auto">
        <table class="w-full text-left text-xs">
          <thead class="border-b border-slate-100 text-slate-500 dark:border-slate-800">
            <tr>
              <th class="px-3 py-2 font-medium">时间</th>
              <th class="px-3 py-2 font-medium">动作</th>
              <th class="px-3 py-2 font-medium">操作者</th>
              <th class="px-3 py-2 font-medium">详情</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="log in logs" :key="log.id" class="border-b border-slate-50 dark:border-slate-800/80">
              <td class="whitespace-nowrap px-3 py-2 font-mono text-slate-500">{{ formatTimestamp(log.timestamp) }}</td>
              <td class="px-3 py-2">
                <NTag size="tiny" :type="log.success ? 'info' : 'error'" :bordered="false">
                  {{ auditActionLabel(log.actionType) }}
                </NTag>
                <span class="ml-1 font-mono text-[10px] text-slate-400">{{ log.actionType }}</span>
              </td>
              <td class="px-3 py-2">{{ actorLabel(log.operator) }}</td>
              <td class="px-3 py-2 text-slate-600 dark:text-slate-300">{{ log.details }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </NCard>
  </div>
</template>
