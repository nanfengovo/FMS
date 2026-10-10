<script setup lang="ts">
import { h, onMounted, ref } from 'vue';
import { NButton, NSelect, NTag } from 'naive-ui';
import { fmsApi } from '@/service/api/fms';

const roleLabels: Record<string, string> = {
  Admin: '管理员',
  Engineer: '工程师',
  Operator: '操作员',
  Viewer: '观察员'
};

const roleOptions = ['Admin', 'Engineer', 'Operator', 'Viewer'].map(role => ({
  label: roleLabels[role],
  value: role
}));

const loading = ref(false);
const savingId = ref('');
const users = ref<Fms.AccessUser[]>([]);
const drafts = ref<Record<string, string[]>>({});

const columns = [
  { title: '用户', key: 'username', width: 160 },
  {
    title: '状态',
    key: 'isActive',
    width: 100,
    render(row: Fms.AccessUser) {
      return h(NTag, { size: 'small', type: row.isActive ? 'success' : 'default' }, () => (row.isActive ? '启用' : '停用'));
    }
  },
  {
    title: '角色',
    key: 'roles',
    render(row: Fms.AccessUser) {
      return h(NSelect, {
        multiple: true,
        value: drafts.value[row.id] || [],
        options: roleOptions,
        'onUpdate:value': (value: string[]) => {
          drafts.value[row.id] = value;
        }
      });
    }
  },
  {
    title: '操作',
    key: 'actions',
    width: 120,
    render(row: Fms.AccessUser) {
      return h(
        NButton,
        {
          size: 'small',
          type: 'primary',
          loading: savingId.value === row.id,
          onClick: () => save(row)
        },
        () => '保存'
      );
    }
  }
];

async function load() {
  loading.value = true;
  try {
    users.value = await fmsApi.getAccessUsers();
    drafts.value = Object.fromEntries(users.value.map(user => [user.id, [...user.roles]]));
  } finally {
    loading.value = false;
  }
}

async function save(user: Fms.AccessUser) {
  const roles = drafts.value[user.id] || [];
  savingId.value = user.id;
  try {
    const result = await fmsApi.replaceUserRoles(user.id, roles);
    if (!result.success) {
      window.$message?.error(result.message || '保存失败');
      return;
    }
    window.$message?.success(`${result.message || '用户角色已更新'}。该用户下次登录后生效。`);
    await load();
  } finally {
    savingId.value = '';
  }
}

onMounted(load);
</script>

<template>
  <div class="w-full p-4">
    <NCard title="用户管理" size="medium" class="shadow-sm">
      <template #header-extra>
        <span class="text-xs text-gray-500">一个用户可以绑定多个角色，权限取这些角色授予的并集。</span>
      </template>
      <NDataTable :columns="columns" :data="users" :loading="loading" :bordered="false" />
    </NCard>
  </div>
</template>
