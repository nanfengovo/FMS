<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import { createFactoryYardRuntime } from './createFactoryYardScene';

defineOptions({
  name: 'FactoryYardCanvas'
});

const props = defineProps<{
  dark: boolean;
}>();

const hostRef = ref<HTMLElement | null>(null);
const backdropRef = ref<HTMLElement | null>(null);
let runtime: ReturnType<typeof createFactoryYardRuntime> | null = null;

// Smooth mouse parallax
let targetX = 0;
let targetY = 0;
let currentX = 0;
let currentY = 0;
let animFrame = 0;

function handleMouseMove(e: MouseEvent) {
  const normX = (e.clientX / window.innerWidth - 0.5) * 2;
  const normY = (e.clientY / window.innerHeight - 0.5) * 2;
  targetX = normX * 12; // 12px max shift
  targetY = normY * 8;
  runtime?.setMouseParallax(normX, normY);
}

function updateParallax() {
  currentX += (targetX - currentX) * 0.05;
  currentY += (targetY - currentY) * 0.05;

  if (backdropRef.value) {
    backdropRef.value.style.transform = `scale(1.04) translate3d(${currentX}px, ${currentY}px, 0)`;
  }
  animFrame = requestAnimationFrame(updateParallax);
}

onMounted(() => {
  if (!hostRef.value) return;
  runtime?.dispose();
  runtime = createFactoryYardRuntime(hostRef.value, props.dark);

  window.addEventListener('mousemove', handleMouseMove, { passive: true });
  updateParallax();
});

watch(
  () => props.dark,
  value => {
    runtime?.setTheme(value);
  }
);

onUnmounted(() => {
  window.removeEventListener('mousemove', handleMouseMove);
  cancelAnimationFrame(animFrame);
  runtime?.dispose();
  runtime = null;
});
</script>

<template>
  <div :class="['factory-yard-container', dark ? 'is-dark' : 'is-light']" aria-hidden="true">
    <!-- Photorealistic Cleanroom Fab Base Backdrop -->
    <div ref="backdropRef" class="cleanroom-backdrop" />
    <!-- Dynamic 3D WebGL Overlay (laser tracks, moving AMRs, 3D HUD badges) -->
    <div ref="hostRef" class="factory-yard-canvas" />
    <!-- Subtle cyber ambient overlay -->
    <div class="ambient-glow" />
  </div>
</template>

<style scoped>
.factory-yard-container {
  position: absolute;
  inset: 0;
  z-index: 0;
  overflow: hidden;
  pointer-events: none;
  background-color: #060c18;
  transition: background-color 0.3s ease;
}

.factory-yard-container.is-light {
  background-color: #eef3f9;
}

.cleanroom-backdrop {
  position: absolute;
  inset: -20px;
  background-image: url('/fab_photoreal.jpg');
  background-position: center 46%;
  background-size: cover;
  background-repeat: no-repeat;
  filter: saturate(1.15) contrast(1.08) brightness(1.03);
  will-change: transform;
  transition: filter 0.3s ease;
}

.factory-yard-container.is-light .cleanroom-backdrop {
  filter: saturate(1.05) contrast(1.02) brightness(1.25);
}

.factory-yard-canvas {
  position: absolute;
  inset: 0;
  z-index: 1;
  pointer-events: none;
}

.ambient-glow {
  position: absolute;
  inset: 0;
  z-index: 2;
  pointer-events: none;
  background:
    radial-gradient(ellipse 95% 80% at 50% 50%, transparent 68%, rgba(4, 10, 20, 0.35) 100%),
    linear-gradient(180deg, rgba(6, 14, 28, 0.35) 0%, transparent 12%, transparent 88%, rgba(6, 14, 28, 0.45) 100%);
  transition: background 0.3s ease;
}

.factory-yard-container.is-light .ambient-glow {
  background:
    radial-gradient(ellipse 95% 80% at 50% 50%, transparent 68%, rgba(200, 220, 245, 0.3) 100%),
    linear-gradient(180deg, rgba(230, 240, 255, 0.4) 0%, transparent 12%, transparent 88%, rgba(230, 240, 255, 0.45) 100%);
}
</style>
