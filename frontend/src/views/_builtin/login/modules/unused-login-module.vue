<script setup lang="ts">
import { useRouterPush } from '@/hooks/common/router';
import { $t } from '@/locales';

defineOptions({
  name: 'UnusedLoginModule'
});

interface Props {
  title: string;
}

defineProps<Props>();

const { toggleLoginModule } = useRouterPush();
</script>

<template>
  <div class="unused-login">
    <p class="unused-login__title">{{ title }}</p>
    <p class="unused-login__hint">{{ $t('page.login.console.unusedHint') }}</p>
    <NButton type="primary" block round @click="toggleLoginModule('pwd-login')">
      {{ $t('page.login.console.backToPwd') }}
    </NButton>
  </div>
</template>

<style scoped>
.unused-login {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 8px 0 4px;
}

.unused-login__title {
  margin: 0;
  font-size: 16px;
  font-weight: 500;
}

.unused-login__hint {
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 1.5;
}
</style>
