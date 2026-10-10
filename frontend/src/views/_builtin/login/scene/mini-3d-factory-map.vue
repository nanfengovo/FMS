<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import * as THREE from 'three';

const props = defineProps<{
  dark?: boolean;
}>();

const containerRef = ref<HTMLDivElement | null>(null);
const isRotating = ref(true);

let renderer: THREE.WebGLRenderer | null = null;
let scene: THREE.Scene | null = null;
let camera: THREE.PerspectiveCamera | null = null;
let animId = 0;
let gridHelper: THREE.GridHelper | null = null;
let ambientLight: THREE.AmbientLight | null = null;
let dirLight: THREE.DirectionalLight | null = null;
let hemiLight: THREE.HemisphereLight | null = null;
let floorMat: THREE.MeshStandardMaterial | null = null;
let resizeObserver: ResizeObserver | null = null;

// Interaction state
let isDragging = false;
let prevMouseX = 0;
let prevMouseY = 0;
let spherical = { radius: 36, theta: Math.PI / 4, phi: Math.PI / 3 };
let targetTheta = Math.PI / 4;
let targetPhi = Math.PI / 3;

// Animated items
interface AnimatedAmr {
  mesh: THREE.Group;
  path: THREE.Vector3[];
  t: number;
  speed: number;
  state: 'running' | 'idle' | 'charging';
}
const amrFleet: AnimatedAmr[] = [];
let pathPulses: THREE.Points[] = [];

function createStationLabel(text: string, color: string): THREE.Sprite {
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 48;
  const ctx = canvas.getContext('2d');
  if (ctx) {
    ctx.clearRect(0, 0, 128, 48);
    ctx.fillStyle = 'rgba(8, 24, 44, 0.85)';
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.roundRect(4, 4, 120, 40, 6);
    ctx.fill();
    ctx.stroke();

    ctx.font = 'bold 20px "IBM Plex Mono", monospace';
    ctx.fillStyle = color;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(text, 64, 24);
  }
  const texture = new THREE.CanvasTexture(canvas);
  const material = new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false });
  const sprite = new THREE.Sprite(material);
  sprite.scale.set(4.2, 1.6, 1);
  return sprite;
}

function createAmrModel(color: number, hasFoup = false): THREE.Group {
  const group = new THREE.Group();

  // Chassis
  const bodyGeo = new THREE.BoxGeometry(2.0, 0.6, 1.4);
  const bodyMat = new THREE.MeshStandardMaterial({
    color: 0x1e293b,
    metalness: 0.8,
    roughness: 0.2
  });
  const body = new THREE.Mesh(bodyGeo, bodyMat);
  body.position.y = 0.35;
  group.add(body);

  // Trim neon band
  const trimGeo = new THREE.BoxGeometry(2.04, 0.12, 1.44);
  const trimMat = new THREE.MeshBasicMaterial({ color });
  const trim = new THREE.Mesh(trimGeo, trimMat);
  trim.position.y = 0.35;
  group.add(trim);

  // Lidar puck
  const lidarGeo = new THREE.CylinderGeometry(0.2, 0.2, 0.25, 16);
  const lidarMat = new THREE.MeshStandardMaterial({ color: 0x0f172a, metalness: 0.9, roughness: 0.1 });
  const lidar = new THREE.Mesh(lidarGeo, lidarMat);
  lidar.position.set(0.6, 0.75, 0);
  group.add(lidar);

  // Lidar beam light
  const beamGeo = new THREE.SphereGeometry(0.08, 8, 8);
  const beamMat = new THREE.MeshBasicMaterial({ color });
  const beam = new THREE.Mesh(beamGeo, beamMat);
  beam.position.set(0.6, 0.85, 0);
  group.add(beam);

  // Wafer FOUP Carrier on top (if carrying cargo)
  if (hasFoup) {
    const foupGeo = new THREE.BoxGeometry(0.9, 0.8, 0.9);
    const foupMat = new THREE.MeshPhysicalMaterial({
      color: 0x00e5ff,
      transmission: 0.6,
      opacity: 0.9,
      transparent: true,
      roughness: 0.1,
      metalness: 0.1,
      emissive: 0x003344,
      emissiveIntensity: 0.3
    });
    const foup = new THREE.Mesh(foupGeo, foupMat);
    foup.position.set(-0.2, 0.95, 0);
    group.add(foup);
  }

  // Underglow shadow / halo
  const glowGeo = new THREE.PlaneGeometry(2.4, 1.8);
  const glowMat = new THREE.MeshBasicMaterial({
    color,
    transparent: true,
    opacity: 0.35,
    depthWrite: false
  });
  const glow = new THREE.Mesh(glowGeo, glowMat);
  glow.rotation.x = -Math.PI / 2;
  glow.position.y = 0.04;
  group.add(glow);

  return group;
}

function initScene() {
  if (!containerRef.value) return;
  const width = containerRef.value.clientWidth || 320;
  const height = containerRef.value.clientHeight || 168;

  // Scene
  scene = new THREE.Scene();

  // Camera
  camera = new THREE.PerspectiveCamera(36, width / height, 0.1, 200);
  updateCameraPos();

  // Renderer
  renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
  renderer.setSize(width, height);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  containerRef.value.appendChild(renderer.domElement);

  // Lights
  ambientLight = new THREE.AmbientLight(props.dark ? 0x90b0d0 : 0xe0e8f5, props.dark ? 0.7 : 1.1);
  scene.add(ambientLight);

  hemiLight = new THREE.HemisphereLight(0x00e5ff, 0x0a192f, props.dark ? 0.4 : 0.6);
  scene.add(hemiLight);

  dirLight = new THREE.DirectionalLight(0xffffff, props.dark ? 1.0 : 1.4);
  dirLight.position.set(20, 30, 15);
  scene.add(dirLight);

  // Base Floor
  const floorGeo = new THREE.PlaneGeometry(36, 26);
  floorMat = new THREE.MeshStandardMaterial({
    color: props.dark ? 0x091220 : 0xd8e4f0,
    metalness: 0.5,
    roughness: 0.4
  });
  const floor = new THREE.Mesh(floorGeo, floorMat);
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -0.01;
  scene.add(floor);

  // Floor Grid
  gridHelper = new THREE.GridHelper(36, 18, props.dark ? 0x00e5ff : 0x005aff, props.dark ? 0x14283d : 0xbed4ec);
  gridHelper.position.y = 0.01;
  scene.add(gridHelper);

  // ── Build Semiconductor Fab Equipment Modules ─────────────────────────────
  buildEquipmentModules();

  // ── Build 3D Laser Path Tracks ───────────────────────────────────────────
  buildTracksAndAmrs();

  // Events
  containerRef.value.addEventListener('mousedown', onMouseDown);
  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mouseup', onMouseUp);
  containerRef.value.addEventListener('wheel', onWheel, { passive: false });

  // Animation Loop
  animate();
}

function buildEquipmentModules() {
  if (!scene) return;

  const eqMatDark = new THREE.MeshStandardMaterial({ color: 0x17253b, metalness: 0.8, roughness: 0.2 });
  const glassMat = new THREE.MeshPhysicalMaterial({
    color: 0x00e5ff,
    transparent: true,
    opacity: 0.6,
    roughness: 0.1,
    transmission: 0.7
  });

  // 1. LITH 光刻区 (Left Front)
  const lithGroup = new THREE.Group();
  lithGroup.position.set(-10, 0, 4);
  const lithBody = new THREE.Mesh(new THREE.BoxGeometry(4.5, 2.6, 3.2), eqMatDark);
  lithBody.position.y = 1.3;
  lithGroup.add(lithBody);
  const lithCol = new THREE.Mesh(new THREE.CylinderGeometry(0.8, 1.0, 1.4, 16), glassMat);
  lithCol.position.set(0, 3.0, 0);
  lithGroup.add(lithCol);
  const lithLabel = createStationLabel('LITH', '#00e5ff');
  lithLabel.position.set(0, 4.2, 0);
  lithGroup.add(lithLabel);
  scene.add(lithGroup);

  // 2. ETCH 刻蚀区 (Left Back)
  const etchGroup = new THREE.Group();
  etchGroup.position.set(-10, 0, -5);
  const etchBody = new THREE.Mesh(new THREE.BoxGeometry(4.5, 2.0, 3.0), eqMatDark);
  etchBody.position.y = 1.0;
  etchGroup.add(etchBody);
  const etchCham1 = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.7, 1.2, 16), glassMat);
  etchCham1.position.set(-1.0, 2.4, 0);
  etchGroup.add(etchCham1);
  const etchCham2 = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.7, 1.2, 16), glassMat);
  etchCham2.position.set(1.0, 2.4, 0);
  etchGroup.add(etchCham2);
  const etchLabel = createStationLabel('ETCH', '#a78bfa');
  etchLabel.position.set(0, 3.6, 0);
  etchGroup.add(etchLabel);
  scene.add(etchGroup);

  // 3. CVD 气相沉积区 (Center Back)
  const cvdGroup = new THREE.Group();
  cvdGroup.position.set(0, 0, -5);
  const cvdBody = new THREE.Mesh(new THREE.BoxGeometry(4.2, 2.4, 3.0), eqMatDark);
  cvdBody.position.y = 1.2;
  cvdGroup.add(cvdBody);
  const cvdDome = new THREE.Mesh(new THREE.SphereGeometry(1.0, 16, 16, 0, Math.PI * 2, 0, Math.PI / 2), glassMat);
  cvdDome.position.set(0, 2.4, 0);
  cvdGroup.add(cvdDome);
  const cvdLabel = createStationLabel('CVD', '#38bdf8');
  cvdLabel.position.set(0, 3.8, 0);
  cvdGroup.add(cvdLabel);
  scene.add(cvdGroup);

  // 4. CMP 抛光区 (Center Front)
  const cmpGroup = new THREE.Group();
  cmpGroup.position.set(0, 0, 4);
  const cmpBody = new THREE.Mesh(new THREE.BoxGeometry(4.2, 1.8, 3.2), eqMatDark);
  cmpBody.position.y = 0.9;
  cmpGroup.add(cmpBody);
  const cmpHead = new THREE.Mesh(new THREE.CylinderGeometry(0.8, 0.8, 0.4, 16), glassMat);
  cmpHead.position.set(0, 2.0, 0);
  cmpGroup.add(cmpHead);
  const cmpLabel = createStationLabel('CMP', '#34d399');
  cmpLabel.position.set(0, 3.2, 0);
  cmpGroup.add(cmpLabel);
  scene.add(cmpGroup);

  // 5. STK 自动立体库 (Right Back)
  const stkGroup = new THREE.Group();
  stkGroup.position.set(10, 0, -4);
  const stkBody = new THREE.Mesh(new THREE.BoxGeometry(4.0, 4.2, 3.5), eqMatDark);
  stkBody.position.y = 2.1;
  stkGroup.add(stkBody);
  // Wafer slot slits
  for (let y = 0.8; y <= 3.6; y += 0.8) {
    const slit = new THREE.Mesh(new THREE.BoxGeometry(3.6, 0.08, 3.6), new THREE.MeshBasicMaterial({ color: 0x00e5ff }));
    slit.position.y = y;
    stkGroup.add(slit);
  }
  const stkLabel = createStationLabel('STK-01', '#38bdf8');
  stkLabel.position.set(0, 4.8, 0);
  stkGroup.add(stkLabel);
  scene.add(stkGroup);

  // 6. CHG 快速充电机集群 (Right Front)
  const chgGroup = new THREE.Group();
  chgGroup.position.set(10, 0, 4.5);
  for (let i = -1.2; i <= 1.2; i += 1.2) {
    const pad = new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.08, 1.2), new THREE.MeshBasicMaterial({ color: 0xf59e0b }));
    pad.position.set(i, 0.04, 0);
    chgGroup.add(pad);
    const post = new THREE.Mesh(new THREE.BoxGeometry(0.3, 1.4, 0.3), eqMatDark);
    post.position.set(i, 0.7, 0.8);
    chgGroup.add(post);
  }
  const chgLabel = createStationLabel('CHG', '#fbbf24');
  chgLabel.position.set(0, 2.5, 0);
  chgGroup.add(chgLabel);
  scene.add(chgGroup);
}

function buildTracksAndAmrs() {
  if (!scene) return;

  // Path Network Points (Loops connecting STK, LITH, ETCH, CVD, CMP, CHG)
  const loop1 = [
    new THREE.Vector3(-10, 0.05, 0),
    new THREE.Vector3(-5, 0.05, 0),
    new THREE.Vector3(0, 0.05, 0),
    new THREE.Vector3(5, 0.05, 0),
    new THREE.Vector3(10, 0.05, 0),
    new THREE.Vector3(10, 0.05, -1),
    new THREE.Vector3(5, 0.05, -1),
    new THREE.Vector3(0, 0.05, -1),
    new THREE.Vector3(-5, 0.05, -1),
    new THREE.Vector3(-10, 0.05, -1)
  ];

  // Draw glowing road tracks
  const roadMat = new THREE.LineBasicMaterial({
    color: props.dark ? 0x00e5ff : 0x005aff,
    linewidth: 2,
    transparent: true,
    opacity: 0.8
  });

  const trackCoords = [
    // Main spine
    [-12, 0, 12, 0],
    [-12, -2, 12, -2],
    [-12, 2, 12, 2],
    // Cross links
    [-10, -5, -10, 5],
    [-5, -5, -5, 5],
    [0, -5, 0, 5],
    [5, -5, 5, 5],
    [10, -5, 10, 5]
  ];

  trackCoords.forEach(([x1, z1, x2, z2]) => {
    const geo = new THREE.BufferGeometry().setFromPoints([
      new THREE.Vector3(x1, 0.02, z1),
      new THREE.Vector3(x2, 0.02, z2)
    ]);
    const line = new THREE.Line(geo, roadMat);
    scene!.add(line);
  });

  // AMR 1: Carrying FOUP from STK to LITH (Cyan)
  const path1 = [
    new THREE.Vector3(8, 0, -2),
    new THREE.Vector3(5, 0, -2),
    new THREE.Vector3(0, 0, -2),
    new THREE.Vector3(-5, 0, -2),
    new THREE.Vector3(-8, 0, -2),
    new THREE.Vector3(-8, 0, 2),
    new THREE.Vector3(-5, 0, 2),
    new THREE.Vector3(0, 0, 2),
    new THREE.Vector3(5, 0, 2),
    new THREE.Vector3(8, 0, 2)
  ];
  const amr1Mesh = createAmrModel(0x00e5ff, true);
  scene.add(amr1Mesh);
  amrFleet.push({ mesh: amr1Mesh, path: path1, t: 0.1, speed: 0.003, state: 'running' });

  // AMR 2: Docking at CVD/CMP (Electric Blue)
  const path2 = [
    new THREE.Vector3(0, 0, 2),
    new THREE.Vector3(0, 0, -2),
    new THREE.Vector3(-5, 0, -2),
    new THREE.Vector3(-5, 0, 2)
  ];
  const amr2Mesh = createAmrModel(0x38bdf8, true);
  scene.add(amr2Mesh);
  amrFleet.push({ mesh: amr2Mesh, path: path2, t: 0.5, speed: 0.0025, state: 'running' });

  // AMR 3: Charging at CHG Dock (Amber)
  const path3 = [new THREE.Vector3(10, 0, 3.5)];
  const amr3Mesh = createAmrModel(0xf59e0b, false);
  amr3Mesh.position.set(10, 0, 3.5);
  scene.add(amr3Mesh);
  amrFleet.push({ mesh: amr3Mesh, path: path3, t: 0, speed: 0, state: 'charging' });

  // AMR 4: Patrol/Idle in Clean corridor
  const path4 = [
    new THREE.Vector3(-10, 0, 2),
    new THREE.Vector3(-10, 0, -2),
    new THREE.Vector3(-5, 0, -2),
    new THREE.Vector3(-5, 0, 2)
  ];
  const amr4Mesh = createAmrModel(0x10b981, false);
  scene.add(amr4Mesh);
  amrFleet.push({ mesh: amr4Mesh, path: path4, t: 0.8, speed: 0.002, state: 'idle' });
}

function updateCameraPos() {
  if (!camera) return;
  const x = spherical.radius * Math.sin(spherical.phi) * Math.sin(spherical.theta);
  const y = spherical.radius * Math.cos(spherical.phi);
  const z = spherical.radius * Math.sin(spherical.phi) * Math.cos(spherical.theta);
  camera.position.set(x, y, z);
  camera.lookAt(0, 1.2, 0);
}

function onMouseDown(e: MouseEvent) {
  isDragging = true;
  prevMouseX = e.clientX;
  prevMouseY = e.clientY;
}

function onMouseMove(e: MouseEvent) {
  if (!isDragging) return;
  const dx = e.clientX - prevMouseX;
  const dy = e.clientY - prevMouseY;
  prevMouseX = e.clientX;
  prevMouseY = e.clientY;

  targetTheta -= dx * 0.01;
  targetPhi = Math.max(0.2, Math.min(Math.PI / 2.1, targetPhi - dy * 0.01));
}

function onMouseUp() {
  isDragging = false;
}

function onWheel(e: WheelEvent) {
  e.preventDefault();
  spherical.radius = Math.max(16, Math.min(60, spherical.radius + e.deltaY * 0.03));
  updateCameraPos();
}

function resetCamera() {
  targetTheta = Math.PI / 4;
  targetPhi = Math.PI / 3;
  spherical.radius = 36;
}

function toggleAutoRotate() {
  isRotating.value = !isRotating.value;
}

function animate() {
  animId = requestAnimationFrame(animate);

  // Auto orbit
  if (isRotating.value && !isDragging) {
    targetTheta += 0.0015;
  }

  // Smooth camera damp
  spherical.theta += (targetTheta - spherical.theta) * 0.08;
  spherical.phi += (targetPhi - spherical.phi) * 0.08;
  updateCameraPos();

  // Update AMRs motion along paths
  amrFleet.forEach(amr => {
    if (amr.path.length <= 1) {
      if (amr.state === 'charging') {
        // Charging pulse
        const pulse = Math.sin(Date.now() * 0.006) * 0.05 + 1;
        amr.mesh.scale.set(pulse, pulse, pulse);
      }
      return;
    }

    amr.t = (amr.t + amr.speed) % 1;
    const totalSegments = amr.path.length;
    const currentIdx = Math.floor(amr.t * totalSegments);
    const nextIdx = (currentIdx + 1) % totalSegments;
    const segmentT = (amr.t * totalSegments) - currentIdx;

    const pA = amr.path[currentIdx];
    const pB = amr.path[nextIdx];
    amr.mesh.position.lerpVectors(pA, pB, segmentT);

    // Direction heading
    const dir = new THREE.Vector3().subVectors(pB, pA).normalize();
    if (dir.lengthSq() > 0.001) {
      const angle = Math.atan2(dir.x, dir.z);
      amr.mesh.rotation.y = angle;
    }
  });

  if (renderer && scene && camera) {
    renderer.render(scene, camera);
  }
}

watch(
  () => props.dark,
  val => {
    if (!scene) return;
    if (ambientLight) ambientLight.color.setHex(val ? 0x90b0d0 : 0xe0e8f5);
    if (floorMat) floorMat.color.setHex(val ? 0x091220 : 0xd8e4f0);
    if (gridHelper) {
      scene.remove(gridHelper);
      gridHelper = new THREE.GridHelper(36, 18, val ? 0x00e5ff : 0x005aff, val ? 0x14283d : 0xbed4ec);
      gridHelper.position.y = 0.01;
      scene.add(gridHelper);
    }
  }
);

function onResize() {
  if (!containerRef.value || !renderer || !camera) return;
  const width = containerRef.value.clientWidth;
  const height = containerRef.value.clientHeight;
  if (width === 0 || height === 0) return;
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  renderer.setSize(width, height);
}

onMounted(() => {
  initScene();
  if (containerRef.value && typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => onResize());
    resizeObserver.observe(containerRef.value);
  }
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  cancelAnimationFrame(animId);
  if (renderer && renderer.domElement) {
    renderer.domElement.remove();
  }
  renderer?.dispose();
});
</script>

<template>
  <div class="mini-3d-wrapper" :class="{ 'is-light': !props.dark }">
    <div ref="containerRef" class="mini-3d-viewport" />
    <!-- Floating mini controls -->
    <div class="mini-3d-toolbar">
      <span class="mini-3d-tag">3D DIGITAL TWIN</span>
      <div class="mini-3d-btns">
        <button class="mini-btn" title="重置视角" @click="resetCamera">⟲ 视角</button>
        <button class="mini-btn" :class="{ 'is-active': isRotating }" title="旋转" @click="toggleAutoRotate">
          {{ isRotating ? '⏸' : '▶' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mini-3d-wrapper {
  position: relative;
  width: 100%;
  height: 164px;
  min-height: 164px;
  flex-shrink: 0;
  border-radius: 8px;
  overflow: hidden;
  background: radial-gradient(circle at 50% 50%, rgba(10, 25, 48, 0.95) 0%, rgba(5, 12, 24, 0.98) 100%);
  border: 1px solid rgba(0, 229, 255, 0.25);
  box-shadow: inset 0 0 20px rgba(0, 229, 255, 0.1);
  cursor: grab;
  margin-bottom: 8px;
}

.mini-3d-wrapper.is-light {
  background: radial-gradient(circle at 50% 50%, rgba(240, 246, 255, 0.95) 0%, rgba(220, 232, 248, 0.98) 100%);
  border-color: rgba(0, 90, 255, 0.25);
  box-shadow: inset 0 0 20px rgba(0, 90, 255, 0.08);
}

.mini-3d-wrapper:active {
  cursor: grabbing;
}

.mini-3d-viewport {
  width: 100%;
  height: 100%;
}

.mini-3d-toolbar {
  position: absolute;
  top: 6px;
  left: 8px;
  right: 8px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  pointer-events: none;
  z-index: 5;
}

.mini-3d-tag {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: #00e5ff;
  background: rgba(0, 20, 40, 0.7);
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid rgba(0, 229, 255, 0.3);
  backdrop-filter: blur(4px);
}

.is-light .mini-3d-tag {
  color: #005aff;
  background: rgba(255, 255, 255, 0.85);
  border-color: rgba(0, 90, 255, 0.3);
}

.mini-3d-btns {
  display: flex;
  gap: 4px;
  pointer-events: auto;
}

.mini-btn {
  background: rgba(0, 30, 60, 0.7);
  border: 1px solid rgba(0, 229, 255, 0.35);
  color: #d7efff;
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s ease;
  font-family: 'IBM Plex Mono', monospace;
}

.is-light .mini-btn {
  background: rgba(255, 255, 255, 0.85);
  border-color: rgba(0, 90, 255, 0.3);
  color: #0a192f;
}

.mini-btn:hover,
.mini-btn.is-active {
  background: rgba(0, 229, 255, 0.25);
  color: #00e5ff;
  border-color: #00e5ff;
}

.is-light .mini-btn:hover,
.is-light .mini-btn.is-active {
  background: rgba(0, 90, 255, 0.15);
  color: #005aff;
  border-color: #005aff;
}
</style>
