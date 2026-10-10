<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';
import { fmsApi } from '@/service/api/fms';

const emit = defineEmits<{
  (e: 'replay-frame', frame: Fms.FlightRecorderFrame): void;
  (e: 'exit-replay'): void;
}>();

const frames = ref<Fms.FlightRecorderFrame[]>([]);
const currentIndex = ref(0);
const currentFrame = ref<Fms.FlightRecorderFrame | null>(null);
const isReplaying = ref(false);
const isPlaying = ref(false);
const playbackSpeed = ref(1.0);
let playTimer: any = null;

async function startReplay() {
  try {
    const list = await fmsApi.getFlightRecorderRecent(30);
    if (!list || list.length === 0) {
      window.$message?.info('近 30 秒还没有可回放的轨迹帧');
      return;
    }
    frames.value = list;
    currentIndex.value = 0;
    currentFrame.value = list[0];
    isReplaying.value = true;
    isPlaying.value = false;
    emit('replay-frame', list[0]);
  } catch (err: any) {
    console.error('Failed to load flight recorder frames:', err);
  }
}

function returnToLive() {
  if (playTimer) clearInterval(playTimer);
  isReplaying.value = false;
  isPlaying.value = false;
  emit('exit-replay');
}

function onSliderScrub(e: Event) {
  const idx = parseInt((e.target as HTMLInputElement).value, 10);
  currentIndex.value = idx;
  if (frames.value[idx]) {
    currentFrame.value = frames.value[idx];
    emit('replay-frame', frames.value[idx]);
  }
}

function step(delta: number) {
  const target = currentIndex.value + delta;
  if (target >= 0 && target < frames.value.length) {
    currentIndex.value = target;
    currentFrame.value = frames.value[target];
    emit('replay-frame', frames.value[target]);
  }
}

function togglePlay() {
  if (isPlaying.value) {
    if (playTimer) clearInterval(playTimer);
    isPlaying.value = false;
  } else {
    isPlaying.value = true;
    runPlaybackLoop();
  }
}

function runPlaybackLoop() {
  if (playTimer) clearInterval(playTimer);
  const interval = Math.max(20, Math.round(100 / playbackSpeed.value)); // default 100ms per tick (10Hz)
  playTimer = setInterval(() => {
    if (currentIndex.value < frames.value.length - 1) {
      currentIndex.value++;
      currentFrame.value = frames.value[currentIndex.value];
      emit('replay-frame', frames.value[currentIndex.value]);
    } else {
      isPlaying.value = false;
      clearInterval(playTimer);
    }
  }, interval);
}

function formatVirtualTime(ms?: number) {
  if (ms === undefined) return '00:00.000';
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  const remMs = ms % 1000;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}.${String(remMs).padStart(3, '0')}`;
}

onBeforeUnmount(() => {
  if (playTimer) clearInterval(playTimer);
});
</script>

<template>
  <div
    class="pointer-events-auto flex items-center gap-4 rounded-xl border border-slate-200 bg-white/95 px-5 py-3 text-slate-800 shadow-lg backdrop-blur dark:border-cyan-500/30 dark:bg-[#0d1117]/95 dark:text-slate-100"
    :class="{ 'border-cyan-400 ring-2 ring-cyan-500/20': isReplaying }"
  >
    <!-- Left: Status & Mode Badge -->
    <div class="flex items-center gap-3 pr-2 border-r border-gray-800">
      <div
        class="flex h-8 w-8 items-center justify-center rounded-lg"
        :class="isReplaying ? 'bg-cyan-500/20 text-cyan-400 animate-pulse' : 'bg-gray-800 text-gray-400'"
      >
        <div class="i-carbon-video text-lg" />
      </div>
      <div>
        <div class="flex items-center gap-1.5">
          <span class="font-mono text-12px font-bold">黑匣子记录仪</span>
          <span
            class="rounded px-1.5 py-0.2 text-9px font-bold tracking-wider"
            :class="isReplaying ? 'bg-cyan-500/30 text-cyan-700 dark:text-cyan-300' : 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400'"
          >
            {{ isReplaying ? '回放中' : '实时画面' }}
          </span>
        </div>
        <div class="font-mono text-10px text-slate-500 dark:text-gray-400">
          <template v-if="frames.length">已捕获 {{ frames.length }} 帧（{{ (frames.length * 0.1).toFixed(1) }} 秒）</template>
          <template v-else>尚未捕获。捕获后才能步进回放。</template>
        </div>
      </div>
    </div>

    <!-- Center: Playback Controls & Scrubber Slider -->
    <div class="flex flex-1 items-center gap-4">
      <!-- Buttons -->
      <div class="flex items-center gap-1.5">
        <button
          class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors"
          title="上一帧 (Step Backward)"
          :disabled="currentIndex <= 0"
          @click="step(-1)"
        >
          <div class="i-carbon-skip-back text-sm" />
        </button>

        <button
          class="flex h-8 w-8 items-center justify-center rounded-full bg-cyan-600 text-white shadow-md hover:bg-cyan-500 transition-colors"
          :title="frames.length ? (isPlaying ? '暂停' : '播放回放') : '先捕获再播放'"
          :disabled="frames.length === 0"
          @click="togglePlay"
        >
          <div :class="isPlaying ? 'i-carbon-pause' : 'i-carbon-play'" class="text-sm" />
        </button>

        <button
          class="rounded p-1.5 text-gray-400 hover:bg-gray-800 hover:text-white transition-colors"
          title="下一帧 (Step Forward)"
          :disabled="currentIndex >= frames.length - 1"
          @click="step(1)"
        >
          <div class="i-carbon-skip-forward text-sm" />
        </button>
      </div>

      <!-- Scrubber Track -->
      <div class="flex flex-1 flex-col gap-1">
        <div class="flex justify-between font-mono text-10px text-gray-400">
          <span>
            帧索引: <span class="text-cyan-400 font-bold">#{{ currentFrame?.tickIndex ?? 0 }}</span>
          </span>
          <span>
            虚拟时间: <span class="text-gray-200">{{ formatVirtualTime(currentFrame?.virtualTimeMs) }}</span>
          </span>
        </div>
        <input
          type="range"
          min="0"
          :max="Math.max(0, frames.length - 1)"
          step="1"
          class="w-full accent-cyan-400 cursor-pointer"
          :value="currentIndex"
          @input="onSliderScrub"
        />
      </div>

      <!-- Speed selector -->
      <div class="flex items-center gap-1 text-10px font-mono">
        <button
          v-for="s in [0.5, 1.0, 2.0, 5.0]"
          :key="s"
          class="rounded px-1.5 py-0.5 transition-colors"
          :class="playbackSpeed === s ? 'bg-cyan-500 text-black font-bold' : 'bg-gray-800 text-gray-400 hover:text-white'"
          @click="playbackSpeed = s"
        >
          {{ s }}x
        </button>
      </div>
    </div>

    <!-- Right: Mode Switch & Reload Buffer -->
    <div class="flex items-center gap-2 pl-2 border-l border-gray-800">
      <button
        v-if="isReplaying"
        class="flex items-center gap-1 rounded bg-emerald-600/30 border border-emerald-500/40 px-3 py-1.5 text-11px font-bold text-emerald-400 hover:bg-emerald-600/50 transition-colors"
        @click="returnToLive"
      >
        <div class="i-carbon-flash" />
        返回实时 (Live)
      </button>

      <button
        v-else
        class="flex items-center gap-1 rounded bg-cyan-600/20 border border-cyan-500/30 px-3 py-1.5 text-11px font-bold text-cyan-400 hover:bg-cyan-600/30 transition-colors"
        @click="startReplay"
      >
        <div class="i-carbon-rewind-30" />
        捕获近30秒回放
      </button>
    </div>
  </div>
</template>
