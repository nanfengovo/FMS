<script setup lang="ts">
defineProps<{ visible: boolean }>();
const emit = defineEmits<{ (e: 'close'): void }>();

const groups = [
  {
    title: '视角',
    rows: [
      ['F', '适配全图'],
      ['+  /  −', '放大 / 缩小'],
      ['方向键', '平移，Shift 加速'],
      ['Q  /  E', '逆时针 / 顺时针旋转 15°'],
      ['Shift + Q / E', '旋转 90°'],
      ['N', '回正北']
    ]
  },
  {
    title: '鼠标',
    rows: [
      ['拖拽', '平移'],
      ['Shift + 拖拽', '旋转视图'],
      ['滚轮', '以光标为中心缩放'],
      ['双击', '聚焦车辆、站点或放大'],
      ['右键', '聚焦、新建任务、复制坐标']
    ]
  },
  {
    title: '操作',
    rows: [
      ['/', '搜索站点、车辆、任务'],
      ['Esc', '取消选择，退出跟随'],
      ['?', '显示或隐藏本列表']
    ]
  }
];
</script>

<template>
  <div
    v-if="visible"
    class="absolute right-4 top-14 z-30 w-[300px] rounded-md border border-slate-200 bg-white/98 p-3 text-[12px] text-slate-700 shadow-xl dark:border-slate-700 dark:bg-[#151b22]/98 dark:text-slate-200"
    @pointerdown.stop
    @wheel.stop
  >
    <div class="mb-2 flex items-center justify-between">
      <span class="text-[13px] font-medium text-slate-900 dark:text-slate-100">快捷键</span>
      <button
        type="button"
        class="flex h-5 w-5 items-center justify-center rounded text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/10"
        @click="emit('close')"
      >
        <div class="i-carbon-close text-12px" />
      </button>
    </div>
    <div v-for="group in groups" :key="group.title" class="mb-2 last:mb-0">
      <div class="mb-1 text-[10px] tracking-wide text-slate-400">{{ group.title }}</div>
      <div v-for="row in group.rows" :key="row[0]" class="flex items-center justify-between gap-3 py-[3px]">
        <span class="text-slate-500">{{ row[1] }}</span>
        <kbd class="shrink-0 rounded border border-slate-200 bg-slate-50 px-1.5 text-[11px] leading-[18px] text-slate-600 dark:border-slate-600 dark:bg-white/5 dark:text-slate-300">
          {{ row[0] }}
        </kbd>
      </div>
    </div>
  </div>
</template>
