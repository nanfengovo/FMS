<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useAuthStore } from '@/store/modules/auth';
import { useNaiveForm, useFormRules } from '@/hooks/common/form';
import { localStg } from '@/utils/storage';
import { $t } from '@/locales';

defineOptions({
  name: 'PwdLogin'
});

const authStore = useAuthStore();
const { formRef, validate } = useNaiveForm();

interface FormModel {
  userName: string;
  password: string;
}

const model: FormModel = reactive({
  userName: 'admin',
  password: 'admin123'
});

const rememberMe = ref(false);
const loginError = ref('');

const rules = computed<Record<keyof FormModel, App.Global.FormRule[]>>(() => {
  const { formRules } = useFormRules();

  return {
    userName: formRules.userName,
    password: formRules.pwd
  };
});

onMounted(() => {
  const remembered = localStg.get('loginUserName');
  if (remembered) {
    model.userName = remembered;
    model.password = '';
    rememberMe.value = true;
  }
});

function persistRememberMe() {
  if (rememberMe.value) {
    localStg.set('loginUserName', model.userName.trim());
  } else {
    localStg.remove('loginUserName');
  }
}

async function handleSubmit() {
  loginError.value = '';
  await validate();
  persistRememberMe();
  await authStore.login(model.userName, model.password);
  if (!authStore.isLogin) {
    loginError.value = $t('page.login.console.loginFailed');
  }
}

type AccountKey = 'admin' | 'operator' | 'safety' | 'viewer';

interface Account {
  key: AccountKey;
  labelKey: App.I18n.I18nKey;
  userName: string;
  password: string;
}

const accounts: Account[] = [
  { key: 'admin', labelKey: 'page.login.pwdLogin.admin', userName: 'admin', password: 'admin123' },
  { key: 'operator', labelKey: 'page.login.pwdLogin.operator', userName: 'operator', password: 'operator123' },
  { key: 'safety', labelKey: 'page.login.pwdLogin.safety', userName: 'safety', password: 'safety123' },
  { key: 'viewer', labelKey: 'page.login.pwdLogin.viewer', userName: 'viewer', password: 'viewer123' }
];

async function handleAccountLogin(account: Account) {
  loginError.value = '';
  model.userName = account.userName;
  model.password = account.password;
  persistRememberMe();
  await authStore.login(account.userName, account.password);
  if (!authStore.isLogin) {
    loginError.value = $t('page.login.console.demoFailed');
  }
}
</script>

<template>
  <NForm ref="formRef" :model="model" :rules="rules" size="large" :show-label="false" @keyup.enter="handleSubmit">
    <NAlert v-if="loginError" type="error" :bordered="false" class="mb-12px" :title="loginError" />
    <NFormItem path="userName">
      <NInput v-model:value="model.userName" :placeholder="$t('page.login.common.userNamePlaceholder')" />
    </NFormItem>
    <NFormItem path="password">
      <NInput
        v-model:value="model.password"
        type="password"
        show-password-on="click"
        :placeholder="$t('page.login.common.passwordPlaceholder')"
      />
    </NFormItem>
    <NSpace vertical :size="18">
      <div class="flex-y-center justify-between">
        <NCheckbox v-model:checked="rememberMe">{{ $t('page.login.pwdLogin.rememberMe') }}</NCheckbox>
        <span class="hint-muted">{{ $t('page.login.console.forgotUnused') }}</span>
      </div>
      <NButton type="primary" size="large" round block :loading="authStore.loginLoading" @click="handleSubmit">
        {{ $t('common.confirm') }}
      </NButton>
      <p class="hint-muted hint-muted--center">{{ $t('page.login.console.unusedCodeRegister') }}</p>
      <NDivider class="account-divider">{{ $t('page.login.console.demoAccounts') }}</NDivider>
      <div class="account-row">
        <NButton v-for="item in accounts" :key="item.key" secondary size="small" @click="handleAccountLogin(item)">
          {{ $t(item.labelKey) }}
        </NButton>
      </div>
    </NSpace>
  </NForm>
</template>

<style scoped>
.hint-muted {
  font-size: 12px;
}

.hint-muted--center {
  margin: 0;
  text-align: center;
}

.account-divider {
  margin: 0 !important;
  font-size: 12px;
}

.account-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}
</style>
