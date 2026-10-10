<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';

export interface MapMenuItem {
  key: string;
  label: string;
  icon?: string;
  hint?: string;
  divider?: boolean;
  disabled?: boolean;
}

const props = withDefaults(
  defineProps<{
    visible: boolean;
    x: number;
    y: number;
    title?: string;
    subtitle?: string;
    items: MapMenuItem[];
    containerWidth?: number;
    containerHeight?: number;
  }>(),
  { title: '', subtitle: '', containerWidth: 0, containerHeight: 0 }
);

const emit = defineEmits<{
  (e: 'select', key: string): void;
  (e: 'close'): void;
}>();

const MENU_WIDTH = 224;

const position = computed(() => {
  const rows = props.items.filter(item => !item.divider).length;
  const dividers = props.items.length - rows;
  const height = (props.title ? 44 : 8) + rows * 32 + dividers * 9 + 8;
  let left = props.x + 2;
  let top = props.y + 2;
  if (props.containerWidth && left + MENU_WIDTH + 8 > props.containerWidth) left = Math.max(8, props.x - MENU_WIDTH - 2);
  if (props.containerHeight && top + height + 8 > props.containerHeight) top = Math.max(8, props.containerHeight - height - 8);
  return { left: `${left}px`, top: `${top}px` };
});

function onDocumentPointerDown(event: PointerEvent) {
  if (!props.visible) return;
  const target = event.target as HTMLElement | null;
  if (target?.closest('[data-map-menu]')) return;
  emit('close');
}

function onKeydown(event: KeyboardEvent) {
  if (props.visible && event.key === 'Escape') emit('close');
}

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown, true);
  document.addEventListener('keydown', onKeydown);
});

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true);
  document.removeEventListener('keydown', onKeydown);
});
</script>

<template>
  <div
    v-if="visible"
    data-map-menu
    class="absolute z-40 select-none overflow-hidden rounded-md border border-slate-200 bg-white/98 py-1 text-[12px] text-slate-700 shadow-xl dark:border-slate-700 dark:bg-[#151b22]/98 dark:text-slate-200"
    :style="{ ...position, width: `${MENU_WIDTH}px` }"
    @contextmenu.prevent
    @pointerdown.stop
  >
    <div v-if="title" class="border-b border-slate-100 px-3 pb-2 pt-1.5 dark:border-slate-800">
      <div class="truncate text-[12px] font-medium text-slate-900 dark:text-slate-100">{{ title }}</div>
      <div v-if="subtitle" class="mt-0.5 truncate text-[11px] text-slate-500">{{ subtitle }}</div>
    </div>
    <template v-for="item in items" :key="item.key">
      <div v-if="item.divider" class="my-1 h-px bg-slate-100 dark:bg-slate-800" />
      <button
        v-else
        type="button"
        class="flex h-8 w-full items-center gap-2.5 px-3 text-left transition-colors"
        :class="
          item.disabled
            ? 'cursor-not-allowed text-slate-300 dark:text-slate-600'
            : 'hover:bg-slate-50 dark:hover:bg-white/8'
        "
        :disabled="item.disabled"
        @click="emit('select', item.key)"
      >
        <div v-if="item.icon" class="shrink-0 text-14px text-slate-400" :class="item.icon" />
        <span class="min-w-0 flex-1 truncate">{{ item.label }}</span>
        <span v-if="item.hint" class="shrink-0 text-[10.5px] text-slate-400">{{ item.hint }}</span>
      </button>
    </template>
  </div>
</template>
