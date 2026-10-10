<script setup lang="ts">
import { h, onMounted, ref } from 'vue';
import { NTag } from 'naive-ui';
import { fmsApi } from '@/service/api/fms';

const loading = ref(false);
const roles = ref<Fms.AccessRole[]>([]);

const columns = [
  { title: '角色', key: 'roleName', width: 140 },
  {
    title: '说明',
    key: 'description',
    minWidth: 180,
    render(row: Fms.AccessRole) {
      return (row.description || '').replaceAll('超级管理员', '管理员');
    }
  },
  {
    title: '已授予',
    key: 'permissions',
    render(row: Fms.AccessRole) {
      if (!row.permissions.length) return '未授予';
      const shown = row.permissions.slice(0, 8);
      return h('div', { class: 'flex flex-wrap gap-1' }, [
        ...shown.map(name => h(NTag, { size: 'small', bordered: false }, () => name)),
        row.permissions.length > shown.length
          ? h(NTag, { size: 'small', title: row.permissions.join('、') }, () => `+${row.permissions.length - shown.length}`)
          : null
      ]);
    }
  }
];

async function load() {
  loading.value = true;
  try {
    roles.value = await fmsApi.getAccessRoles();
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="w-full p-4">
    <NCard title="角色管理" size="medium" class="shadow-sm">
      <template #header-extra>
        <span class="text-xs text-gray-500">用户只绑定角色。菜单和按钮的授予在「菜单管理」里修改。</span>
      </template>
      <NDataTable :columns="columns" :data="roles" :loading="loading" :bordered="false" />
    </NCard>
  </div>
</template>
