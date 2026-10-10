<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { SEARCH_KIND_LABELS, searchItems } from './search';
import type { SearchHit, SearchItem, SearchKind } from './search';

const props = withDefaults(
  defineProps<{
    items?: SearchItem[];
    placeholder?: string;
  }>(),
  { items: () => [], placeholder: '搜索站点、车辆、任务' }
);

const emit = defineEmits<{
  (e: 'pick', item: SearchItem): void;
}>();

const rootRef = ref<HTMLDivElement | null>(null);
const inputRef = ref<HTMLInputElement | null>(null);
const query = ref('');
const open = ref(false);
const focused = ref(false);
const active = ref(0);

const hits = computed<SearchHit[]>(() => searchItems(props.items, query.value));

const groups = computed(() => {
  const order: SearchKind[] = ['vehicle', 'order', 'node'];
  return order
    .map(kind => ({ kind, label: SEARCH_KIND_LABELS[kind], rows: hits.value.filter(hit => hit.kind === kind) }))
    .filter(group => group.rows.length > 0);
});

const flat = computed(() => groups.value.flatMap(group => group.rows));

watch(hits, () => {
  active.value = 0;
});

const ICONS: Record<SearchKind, string> = {
  vehicle: 'i-carbon-car',
  order: 'i-carbon-task',
  node: 'i-carbon-location'
};

function pick(item: SearchItem) {
  emit('pick', item);
  open.value = false;
  query.value = '';
  inputRef.value?.blur();
}

function clear() {
  query.value = '';
  inputRef.value?.focus();
}

function onKeydown(event: KeyboardEvent) {
  event.stopPropagation();
  if (event.key === 'ArrowDown') {
    event.preventDefault();
    if (flat.value.length) active.value = (active.value + 1) % flat.value.length;
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    if (flat.value.length) active.value = (active.value - 1 + flat.value.length) % flat.value.length;
  } else if (event.key === 'Enter') {
    event.preventDefault();
    const hit = flat.value[active.value];
    if (hit) pick(hit);
  } else if (event.key === 'Escape') {
    event.preventDefault();
    if (query.value) query.value = '';
    else inputRef.value?.blur();
    open.value = false;
  }
}

function onDocumentPointerDown(event: PointerEvent) {
  if (rootRef.value && !rootRef.value.contains(event.target as Node)) open.value = false;
}

onMounted(() => document.addEventListener('pointerdown', onDocumentPointerDown, true));
onBeforeUnmount(() => document.removeEventListener('pointerdown', onDocumentPointerDown, true));

function focus() {
  inputRef.value?.focus();
  inputRef.value?.select();
  open.value = true;
}

defineExpose({ focus });

function rowIndex(hit: SearchHit) {
  return flat.value.indexOf(hit);
}
</script>

<template>
  <div ref="rootRef" class="relative w-[264px] text-[12px]" @pointerdown.stop @wheel.stop>
    <div
      class="flex h-8 items-center gap-2 rounded-md border bg-white/95 px-2.5 text-slate-700 shadow-sm transition-colors dark:bg-[#151b22]/95 dark:text-slate-200"
      :class="focused ? 'border-blue-500 dark:border-blue-400' : 'border-slate-200 dark:border-slate-700'"
    >
      <div class="i-carbon-search shrink-0 text-14px text-slate-400" />
      <input
        ref="inputRef"
        v-model="query"
        type="text"
        autocomplete="off"
        spellcheck="false"
        class="min-w-0 flex-1 bg-transparent text-[12px] outline-none placeholder:text-slate-400"
        :placeholder="placeholder"
        @focus="focused = true; open = true"
        @blur="focused = false"
        @keydown="onKeydown"
      />
      <button
        v-if="query"
        type="button"
        class="flex h-5 w-5 items-center justify-center rounded text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/10 dark:hover:text-slate-200"
        title="清空"
        @click="clear"
      >
        <div class="i-carbon-close text-12px" />
      </button>
      <kbd
        v-else
        class="rounded border border-slate-200 px-1.5 text-[10px] leading-[16px] text-slate-400 dark:border-slate-600"
      >
        /
      </kbd>
    </div>

    <div
      v-if="open && query.trim()"
      class="absolute left-0 right-0 top-full z-20 mt-1 max-h-[320px] overflow-auto rounded-md border border-slate-200 bg-white/98 py-1 shadow-lg dark:border-slate-700 dark:bg-[#151b22]/98"
    >
      <template v-for="group in groups" :key="group.kind">
        <div class="px-3 pb-0.5 pt-1.5 text-[10px] tracking-wide text-slate-400">{{ group.label }}</div>
        <button
          v-for="hit in group.rows"
          :key="`${hit.kind}:${hit.id}`"
          type="button"
          class="flex w-full items-center gap-2.5 px-3 py-1.5 text-left transition-colors"
          :class="rowIndex(hit) === active ? 'bg-blue-50 dark:bg-blue-500/15' : 'hover:bg-slate-50 dark:hover:bg-white/5'"
          @mouseenter="active = rowIndex(hit)"
          @click="pick(hit)"
        >
          <div class="shrink-0 text-14px text-slate-400" :class="ICONS[hit.kind]" />
          <div class="min-w-0 flex-1">
            <div class="truncate text-slate-900 dark:text-slate-100">{{ hit.title }}</div>
            <div v-if="hit.subtitle" class="truncate text-[11px] text-slate-500">{{ hit.subtitle }}</div>
          </div>
        </button>
      </template>
      <div v-if="!flat.length" class="px-3 py-3 text-slate-400">没有匹配的车辆、任务或站点</div>
    </div>
  </div>
</template>
