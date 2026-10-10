<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue';
import { NTag, type TreeOption } from 'naive-ui';
import { fmsApi } from '@/service/api/fms';
import { useAuthStore } from '@/store/modules/auth';

const authStore = useAuthStore();
const loading = ref(false);
const saving = ref(false);
const roles = ref<Fms.AccessRole[]>([]);
const catalog = ref<Fms.PermissionNode[]>([]);
const selectedRole = ref('Operator');
const checkedKeys = ref<Array<string | number>>([]);

function isButton(node: Fms.PermissionNode) {
  return node.kind === 1 || node.kind === 'Button';
}

const treeData = computed<TreeOption[]>(() => {
  const byParent = new Map<string, Fms.PermissionNode[]>();
  for (const node of catalog.value) {
    const parent = node.parent || '';
    const list = byParent.get(parent) || [];
    list.push(node);
    byParent.set(parent, list);
  }

  function build(parent: string): TreeOption[] {
    return (byParent.get(parent) || []).map(node => {
      const children = build(node.name);
      return {
        key: node.name,
        label: `${node.displayName}  ${node.name}`,
        prefix: () =>
          h(
            NTag,
            { size: 'tiny', bordered: false, type: isButton(node) ? 'warning' : 'info' },
            () => (isButton(node) ? '按钮' : '菜单')
          ),
        children: children.length ? children : undefined
      };
    });
  }

  return build('');
});

const roleOptions = computed(() => roles.value.map(role => ({ label: `${role.roleName} · ${role.description}`, value: role.roleName })));

function loadChecked() {
  const role = roles.value.find(item => item.roleName === selectedRole.value);
  checkedKeys.value = [...(role?.permissions || [])];
}

async function load() {
  loading.value = true;
  try {
    const [roleList, nodes] = await Promise.all([fmsApi.getAccessRoles(), fmsApi.getAccessCatalog()]);
    roles.value = roleList;
    catalog.value = nodes;
    if (!roles.value.some(item => item.roleName === selectedRole.value) && roles.value[0]) {
      selectedRole.value = roles.value[0].roleName;
    }
    loadChecked();
  } finally {
    loading.value = false;
  }
}

watch(selectedRole, loadChecked);

async function save() {
  const selected = new Set(checkedKeys.value.map(key => String(key)));
  for (const node of catalog.value) {
    if (node.parent && selected.has(node.name)) selected.add(node.parent);
  }

  saving.value = true;
  try {
    const result = await fmsApi.replaceRoleGrants(selectedRole.value, [...selected]);
    if (!result.success) {
      window.$message?.error(result.message || '保存失败');
      return;
    }
    window.$message?.success(result.message || '菜单和按钮权限已更新，下一个请求即生效');
    await load();
    if ((authStore.userInfo.roles || []).includes(selectedRole.value)) {
      await authStore.initUserInfo();
      window.$message?.info('当前账号的按钮状态已更新。侧边菜单刷新页面后按新授予显示。');
    }
  } finally {
    saving.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="w-full p-4">
    <NCard title="菜单管理" size="medium" class="shadow-sm">
      <template #header-extra>
        <span class="text-xs text-gray-500">勾选菜单控制能否进入页面，勾选按钮控制页面里的操作和对应接口。</span>
      </template>
      <div class="space-y-4">
        <div class="sticky top-0 z-10 flex items-center gap-3 bg-white py-2 dark:bg-[#11151f]">
          <NSelect v-model:value="selectedRole" :options="roleOptions" :loading="loading" class="flex-1" />
          <NButton type="primary" :loading="saving" :disabled="!selectedRole" @click="save">保存授予</NButton>
        </div>
        <NSpin :show="loading">
          <NTree
            v-model:checked-keys="checkedKeys"
            block-line
            checkable
            :cascade="false"
            :data="treeData"
          />
        </NSpin>
      </div>
    </NCard>
  </div>
</template>
