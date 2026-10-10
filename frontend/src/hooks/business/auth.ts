import { computed } from 'vue';
import { useAuthStore } from '@/store/modules/auth';

const roleLabels: Record<string, string> = {
  Admin: '管理员',
  Engineer: '调度工程师',
  Operator: '现场操作员',
  Viewer: '只读访客',
  R_SUPER: '管理员',
  R_ENGINEER: '调度工程师',
  R_OPERATOR: '现场操作员',
  R_VIEWER: '只读访客',
  SafetyOfficer: '调度工程师',
  Supervisor: '调度工程师'
};

export function useAuth() {
  const authStore = useAuthStore();

  const currentRoleInfo = computed(() => {
    const roles = authStore.userInfo.roles || [];
    const label = roles.map(role => roleLabels[role] || role).join('、') || '未授权';
    return { label };
  });

  function hasAuth(codes: string | string[]) {
    const allowed = authStore.userInfo.buttons || [];
    if (typeof codes === 'string') {
      return allowed.includes(codes);
    }
    return codes.some(code => allowed.includes(code));
  }

  function checkPermission(code: string, actionName: string = '该操作'): boolean {
    if (hasAuth(code)) return true;
    window.$message?.warning(`权限受控：当前角色【${currentRoleInfo.value.label}】暂无权限执行【${actionName}】`);
    return false;
  }

  return {
    currentRoleInfo,
    hasAuth,
    checkPermission
  };
}
