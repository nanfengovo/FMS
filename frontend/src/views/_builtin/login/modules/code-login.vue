<script setup lang="ts">
import { $t } from '@/locales';
import UnusedLoginModule from './unused-login-module.vue';

defineOptions({
  name: 'CodeLogin'
});
</script>

<template>
  <UnusedLoginModule :title="$t('page.login.console.unusedCode')" />
</template>
