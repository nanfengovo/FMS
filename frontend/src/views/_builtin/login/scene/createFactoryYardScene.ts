import * as THREE from 'three';

/**
 * 300mm Advanced Semiconductor Cleanroom Fab Scene (V2 Digital Twin)
 * Accurately models the 12-inch Wafer Fabrication Facility (Fab) & AMR Fleet
 * Matching target reference (media_1791255332486.jpg):
 * - Cleanroom steel-slate high-gloss epoxy floor with antistatic seams & safety bay borders
 * - Foreground "物料存储区 (FOUP/Carrier)" stocker rack with glowing 300mm FOUP wafer pods
 * - Dual Lithography (光刻区) ASML-style immersion scanners with amber safelight windows & loadports
 * - Etch (刻蚀区) multi-chamber vacuum cluster tools with cylindrical reactor modules
 * - CVD (沉积区) furnace towers & CMP chemical mechanical polishing equipment
 * - Overhead cleanroom cable ladder trays & structural pillars with hazard warning chevrons
 * - Glowing dual-lane neon-cyan laser navigation highway with turn fillets & directional chevrons
 * - Semiconductor cleanroom AMR fleet with spinning alloy wheels & active FOUP wafer pods
 * - Floating 3D holographic HUD badges anchored to each AMR (AMR-003, AMR-012, AMR-017, AMR-028, AMR-031)
 * - Floating 3D Spatial Zone signage (LITHOGRAPHY 光刻区, ETCH 刻蚀区, CVD 沉积区, 物料存储区)
 */

function disposeObject(root: { traverse: (cb: (obj: any) => void) => void }) {
  const geometries = new Set<any>();
  const materials = new Set<any>();
  const textures = new Set<any>();

  root.traverse(obj => {
    const mesh = obj as { geometry?: any; material?: any };
    if (mesh.geometry) geometries.add(mesh.geometry);
    const list = Array.isArray(mesh.material) ? mesh.material : mesh.material ? [mesh.material] : [];
    for (const item of list) {
      materials.add(item);
      if (item?.map) textures.add(item.map);
      if (item?.emissiveMap) textures.add(item.emissiveMap);
      if (item?.roughnessMap) textures.add(item.roughnessMap);
    }
  });

  geometries.forEach(item => item.dispose?.());
  textures.forEach(item => item.dispose?.());
  materials.forEach(item => item.dispose?.());
}

// ── Cleanroom High-Gloss Epoxy Floor Texture ──────────────────────────────────
function makeCleanroomFloorTexture() {
  const size = 2048;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d');
  const tex = new THREE.CanvasTexture(canvas);
  if (!ctx) return tex;
  const context = ctx;

  // 1. Cleanroom Metallic Blue-Grey Epoxy Base
  context.fillStyle = '#263648';
  context.fillRect(0, 0, size, size);

  // 2. Micro cleanroom conductive speckles
  for (let i = 0; i < 7000; i++) {
    const x = Math.random() * size;
    const y = Math.random() * size;
    const alpha = 0.04 + Math.random() * 0.08;
    context.fillStyle = `rgba(200, 230, 255, ${alpha})`;
    context.fillRect(x, y, 1.4, 1.4);
  }

  // 3. 600mm cleanroom raised floor tile grid (every 64px = ~600mm)
  context.strokeStyle = 'rgba(75, 115, 155, 0.28)';
  context.lineWidth = 1.2;
  for (let i = 0; i <= size; i += 64) {
    context.beginPath();
    context.moveTo(i, 0);
    context.lineTo(i, size);
    context.stroke();

    context.beginPath();
    context.moveTo(0, i);
    context.lineTo(size, i);
    context.stroke();
  }

  // 4. Primary Cleanroom Bay Grid (every 256px)
  context.strokeStyle = 'rgba(0, 229, 255, 0.16)';
  context.lineWidth = 2.2;
  for (let i = 0; i <= size; i += 256) {
    context.beginPath();
    context.moveTo(i, 0);
    context.lineTo(i, size);
    context.stroke();

    context.beginPath();
    context.moveTo(0, i);
    context.lineTo(size, i);
    context.stroke();
  }

  // 5. Equipment Bay Safety Markings (Yellow hatched safety borders)
  function drawSafetyZone(rx: number, ry: number, rw: number, rh: number, label: string) {
    context.save();
    context.strokeStyle = 'rgba(240, 180, 40, 0.55)';
    context.lineWidth = 3;
    context.setLineDash([16, 12]);
    context.strokeRect(rx, ry, rw, rh);
    context.setLineDash([]);

    // Corner L-brackets
    context.strokeStyle = '#00e5ff';
    context.lineWidth = 2.5;
    const bLen = 20;
    // top-left
    context.beginPath();
    context.moveTo(rx, ry + bLen); context.lineTo(rx, ry); context.lineTo(rx + bLen, ry);
    context.stroke();
    // top-right
    context.beginPath();
    context.moveTo(rx + rw - bLen, ry); context.lineTo(rx + rw, ry); context.lineTo(rx + rw, ry + bLen);
    context.stroke();
    // bottom-left
    context.beginPath();
    context.moveTo(rx, ry + rh - bLen); context.lineTo(rx, ry + rh); context.lineTo(rx + bLen, ry + rh);
    context.stroke();
    // bottom-right
    context.beginPath();
    context.moveTo(rx + rw - bLen, ry + rh); context.lineTo(rx + rw, ry + rh); context.lineTo(rx + rw, ry + rh - bLen);
    context.stroke();

    // Zone ID text on floor
    context.fillStyle = 'rgba(160, 215, 255, 0.45)';
    context.font = 'bold 18px monospace';
    context.fillText(label, rx + 16, ry + 28);
    context.restore();
  }

  // Lithography Bay
  drawSafetyZone(150, 160, 720, 520, 'BAY-01 // LITHOGRAPHY');
  // Etch Bay
  drawSafetyZone(980, 160, 840, 540, 'BAY-02 // ETCH CLUSTERS');
  // FOUP Stocker Storage
  drawSafetyZone(220, 1200, 720, 520, 'STK-01 // FOUP CARRIER STAGING');
  // Packaging Bay
  drawSafetyZone(1180, 1200, 680, 520, 'BAY-03 // PACKAGING & CMP');

  tex.colorSpace = THREE.SRGBColorSpace;
  tex.wrapS = THREE.RepeatWrapping;
  tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(1, 1);
  return tex;
}

// ── Shared Materials ──────────────────────────────────────────────────────────
function createMaterials() {
  const floorTex = makeCleanroomFloorTexture();

  return {
    // High-spec cleanroom epoxy floor (sleek, reflective, satin-gloss)
    floor: new THREE.MeshPhysicalMaterial({
      map: floorTex,
      color: 0x344658,
      roughness: 0.16,
      metalness: 0.25,
      clearcoat: 0.85,
      clearcoatRoughness: 0.14
    }),
    // Cleanroom white equipment panels (ASML/Lam cleanroom casing)
    cleanWhite: new THREE.MeshStandardMaterial({
      color: 0xf2f7fc,
      roughness: 0.22,
      metalness: 0.18
    }),
    // Cleanroom machine dark charcoal chassis
    machineDark: new THREE.MeshStandardMaterial({
      color: 0x222e3c,
      roughness: 0.38,
      metalness: 0.42
    }),
    // Polished stainless steel (flanges, pipes, vacuum chambers)
    stainlessSteel: new THREE.MeshStandardMaterial({
      color: 0xd8e6f0,
      roughness: 0.15,
      metalness: 0.92
    }),
    // Yellow/Amber Lithography Safelight inspection glass
    lithoYellowGlass: new THREE.MeshPhysicalMaterial({
      color: 0xffaa00,
      emissive: 0xff9900,
      emissiveIntensity: 0.95,
      transparent: true,
      opacity: 0.85,
      roughness: 0.08,
      metalness: 0.05
    }),
    // Vacuum observation dark glass
    vacuumGlass: new THREE.MeshPhysicalMaterial({
      color: 0x061824,
      emissive: 0x0099dd,
      emissiveIntensity: 0.65,
      transparent: true,
      opacity: 0.82,
      roughness: 0.1,
      metalness: 0.12
    }),
    // FOUP 300mm wafer carrier outer shell
    foupShell: new THREE.MeshPhysicalMaterial({
      color: 0x08283e,
      emissive: 0x031828,
      emissiveIntensity: 0.35,
      transparent: true,
      opacity: 0.84,
      roughness: 0.1,
      metalness: 0.18,
      clearcoat: 0.95,
      clearcoatRoughness: 0.08
    }),
    // Silicon wafer glowing edges inside FOUP
    waferDisc: new THREE.MeshStandardMaterial({
      color: 0x00e5ff,
      emissive: 0x00e5ff,
      emissiveIntensity: 3.2,
      roughness: 0.08,
      metalness: 0.35
    }),
    // Navigation path outer glow ribbon
    pathGlow: new THREE.MeshBasicMaterial({
      color: 0x00e5ff,
      transparent: true,
      opacity: 0.38,
      depthWrite: false
    }),
    // Navigation path bright center laser line
    pathCore: new THREE.MeshBasicMaterial({
      color: 0xc8f9ff,
      transparent: true,
      opacity: 0.98
    }),
    // AMR body graphite panel
    amrBody: new THREE.MeshStandardMaterial({
      color: 0x1b232c,
      roughness: 0.32,
      metalness: 0.5
    }),
    // AMR top white cleanroom cover
    amrWhite: new THREE.MeshStandardMaterial({
      color: 0xf5f9fd,
      roughness: 0.2,
      metalness: 0.15
    }),
    // Tire rubber
    tire: new THREE.MeshStandardMaterial({
      color: 0x101214,
      roughness: 0.92,
      metalness: 0.02
    }),
    // Wheel alloy hub
    hub: new THREE.MeshStandardMaterial({
      color: 0x98b2c4,
      roughness: 0.2,
      metalness: 0.8
    }),
    // Drop shadow
    shadow: new THREE.MeshBasicMaterial({
      color: 0x000000,
      transparent: true,
      opacity: 0.48,
      depthWrite: false
    }),
    // Headlight cone on floor
    headlightCone: new THREE.MeshBasicMaterial({
      color: 0x7ef5ff,
      transparent: true,
      opacity: 0.25,
      depthWrite: false
    }),
    // Indicator LEDs
    ledCyan: new THREE.MeshBasicMaterial({ color: 0x00e5ff }),
    ledGreen: new THREE.MeshBasicMaterial({ color: 0x3ee0c8 }),
    ledAmber: new THREE.MeshBasicMaterial({ color: 0xf0b429 }),
    ledRed: new THREE.MeshBasicMaterial({ color: 0xf07178 })
  };
}

type Mats = ReturnType<typeof createMaterials>;

// ── 300mm FOUP (Front Opening Unified Pod) Wafer Carrier ──────────────────────
function createFoup(mats: Mats) {
  const foup = new THREE.Group();

  // 1. Outer Polycarbonate Pod Shell
  const shellGeo = new THREE.BoxGeometry(0.42, 0.36, 0.40);
  const shell = new THREE.Mesh(shellGeo, mats.foupShell);
  shell.position.y = 0.18;
  shell.castShadow = true;
  foup.add(shell);

  // 2. Robotic Handling Flange / Top Handle
  const handleGeo = new THREE.BoxGeometry(0.14, 0.035, 0.06);
  const handle = new THREE.Mesh(handleGeo, mats.stainlessSteel);
  handle.position.set(0, 0.38, 0);
  foup.add(handle);

  // 3. Side Grip Rails
  const railGeo = new THREE.BoxGeometry(0.02, 0.025, 0.32);
  const railLeft = new THREE.Mesh(railGeo, mats.machineDark);
  railLeft.position.set(-0.215, 0.18, 0);
  foup.add(railLeft);
  const railRight = new THREE.Mesh(railGeo, mats.machineDark);
  railRight.position.set(0.215, 0.18, 0);
  foup.add(railRight);

  // 4. Internal Silicon Wafers (Glowing stacked discs inside the carrier)
  const waferGeo = new THREE.CylinderGeometry(0.14, 0.14, 0.007, 16);
  for (let i = 0; i < 6; i++) {
    const wafer = new THREE.Mesh(waferGeo, mats.waferDisc);
    wafer.position.set(0, 0.08 + i * 0.038, 0);
    foup.add(wafer);
  }

  // 5. Front RFID / Barcode Label & Status Glow
  const labelGeo = new THREE.PlaneGeometry(0.16, 0.04);
  const label = new THREE.Mesh(labelGeo, mats.ledCyan);
  label.position.set(0, 0.28, 0.202);
  foup.add(label);

  return foup;
}

// ── 物料存储区: Foreground FOUP Stocker / Staging Rack Bay ───────────────────
function createFoupStockerBay(mats: Mats) {
  const stocker = new THREE.Group();

  const rackPostGeo = new THREE.BoxGeometry(0.06, 1.8, 0.06);
  const rackBeamXGeo = new THREE.BoxGeometry(3.6, 0.05, 0.06);
  const rackBeamZGeo = new THREE.BoxGeometry(0.06, 0.05, 0.9);
  const shelfPlateGeo = new THREE.BoxGeometry(3.5, 0.025, 0.86);

  // 6 Vertical structural posts
  const postPositions = [
    [-1.75, -0.42], [0, -0.42], [1.75, -0.42],
    [-1.75, 0.42],  [0, 0.42],  [1.75, 0.42]
  ];
  for (const [x, z] of postPositions) {
    const post = new THREE.Mesh(rackPostGeo, mats.stainlessSteel);
    post.position.set(x, 0.9, z);
    post.castShadow = true;
    stocker.add(post);
  }

  // 3 Shelf tiers
  const tierHeights = [0.15, 0.72, 1.28];
  for (const y of tierHeights) {
    const beamFront = new THREE.Mesh(rackBeamXGeo, mats.machineDark);
    beamFront.position.set(0, y, 0.42);
    stocker.add(beamFront);

    const beamRear = new THREE.Mesh(rackBeamXGeo, mats.machineDark);
    beamRear.position.set(0, y, -0.42);
    stocker.add(beamRear);

    for (const x of [-1.75, 0, 1.75]) {
      const beamZ = new THREE.Mesh(rackBeamZGeo, mats.machineDark);
      beamZ.position.set(x, y, 0);
      stocker.add(beamZ);
    }

    const shelf = new THREE.Mesh(shelfPlateGeo, mats.machineDark);
    shelf.position.set(0, y + 0.025, 0);
    shelf.receiveShadow = true;
    stocker.add(shelf);

    // Glowing Cyan Status LED Light Strip along front of each shelf
    const ledStripGeo = new THREE.BoxGeometry(3.5, 0.018, 0.018);
    const ledStrip = new THREE.Mesh(ledStripGeo, mats.ledCyan);
    ledStrip.position.set(0, y + 0.025, 0.455);
    stocker.add(ledStrip);
  }

  // Populate shelves with glowing FOUP wafer carriers
  const xSlots = [-1.3, -0.78, -0.26, 0.26, 0.78, 1.3];
  for (let t = 0; t < 3; t++) {
    const y = tierHeights[t] + 0.038;
    for (let s = 0; s < xSlots.length; s++) {
      if ((t === 1 && s === 2) || (t === 2 && s === 0) || (t === 2 && s === 4) || (t === 0 && s === 5)) continue;
      const foup = createFoup(mats);
      foup.position.set(xSlots[s], y, 0);
      stocker.add(foup);
    }
  }

  // Stocker floor contact shadow
  const shadowGeo = new THREE.PlaneGeometry(3.8, 1.1);
  const shadow = new THREE.Mesh(shadowGeo, mats.shadow);
  shadow.rotation.x = -Math.PI / 2;
  shadow.position.y = 0.005;
  stocker.add(shadow);

  return stocker;
}

// ── LITHOGRAPHY: ASML-Style Immersion Scanner ────────────────────────────────
function createLithoScanner(mats: Mats) {
  const litho = new THREE.Group();

  // 1. Main Clean Enclosure
  const bodyGeo = new THREE.BoxGeometry(3.8, 2.6, 2.2);
  const body = new THREE.Mesh(bodyGeo, mats.cleanWhite);
  body.position.y = 1.35;
  body.castShadow = true;
  body.receiveShadow = true;
  litho.add(body);

  // 2. Base Vibration Isolation Plinth
  const plinthGeo = new THREE.BoxGeometry(4.0, 0.16, 2.4);
  const plinth = new THREE.Mesh(plinthGeo, mats.machineDark);
  plinth.position.y = 0.08;
  plinth.castShadow = true;
  litho.add(plinth);

  // 3. Top HEPA Fan Filter Units (FFU Grid on Roof)
  const ffuGeo = new THREE.BoxGeometry(3.6, 0.25, 2.0);
  const ffu = new THREE.Mesh(ffuGeo, mats.machineDark);
  ffu.position.set(0, 2.78, 0);
  litho.add(ffu);

  // 4. Yellow/Amber Photoresist Safelight Inspection Window
  const windowGeo = new THREE.BoxGeometry(1.6, 0.8, 0.04);
  const win = new THREE.Mesh(windowGeo, mats.lithoYellowGlass);
  win.position.set(-0.6, 1.6, 1.11);
  litho.add(win);

  // Amber interior glow light
  const amberLight = new THREE.PointLight(0xffa500, 1.8, 4.0, 2);
  amberLight.position.set(-0.6, 1.6, 0.8);
  litho.add(amberLight);

  // 5. Automated FOUP Load Ports (Front Interface)
  for (const lx of [0.6, 1.3]) {
    const portBaseGeo = new THREE.BoxGeometry(0.55, 0.72, 0.45);
    const portBase = new THREE.Mesh(portBaseGeo, mats.machineDark);
    portBase.position.set(lx, 0.42, 1.32);
    litho.add(portBase);

    if (lx === 0.6) {
      const foup = createFoup(mats);
      foup.position.set(lx, 0.78, 1.32);
      litho.add(foup);
    }

    const portLed = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.02, 0.02), mats.ledCyan);
    portLed.position.set(lx, 0.8, 1.55);
    litho.add(portLed);
  }

  // 6. Overhead Utility Piping
  const pipeGeo = new THREE.CylinderGeometry(0.06, 0.06, 1.2, 12);
  for (const px of [-1.2, -0.9, 0.9, 1.2]) {
    const pipe = new THREE.Mesh(pipeGeo, mats.stainlessSteel);
    pipe.position.set(px, 3.2, 0);
    litho.add(pipe);
  }

  // 7. Cleanroom 3-Color Light Tower
  const poleGeo = new THREE.CylinderGeometry(0.015, 0.015, 0.45, 8);
  const pole = new THREE.Mesh(poleGeo, mats.stainlessSteel);
  pole.position.set(1.7, 2.9, 0.9);
  litho.add(pole);

  const greenBeacon = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.06, 8), mats.ledGreen);
  greenBeacon.position.set(1.7, 3.12, 0.9);
  litho.add(greenBeacon);

  return litho;
}

// ── ETCH: Multi-Chamber Vacuum Cluster Tool (Lam/TEL Style) ───────────────────
function createEtchClusterTool(mats: Mats) {
  const etch = new THREE.Group();

  // 1. Central Vacuum Transfer Chamber (Hexagonal Hub)
  const hubGeo = new THREE.CylinderGeometry(1.2, 1.2, 0.9, 6);
  const hub = new THREE.Mesh(hubGeo, mats.stainlessSteel);
  hub.position.y = 0.9;
  hub.castShadow = true;
  etch.add(hub);

  const hubCapGeo = new THREE.CylinderGeometry(0.45, 0.45, 0.06, 16);
  const hubCap = new THREE.Mesh(hubCapGeo, mats.vacuumGlass);
  hubCap.position.y = 1.38;
  etch.add(hubCap);

  // 2. Radial Vacuum Process Chambers (4 cylindrical reactor modules)
  const angles = [-Math.PI * 0.75, -Math.PI * 0.25, Math.PI * 0.25, Math.PI * 0.75];
  for (const angle of angles) {
    const chamberGroup = new THREE.Group();
    chamberGroup.position.set(Math.cos(angle) * 1.55, 0.9, Math.sin(angle) * 1.55);

    const cGeo = new THREE.CylinderGeometry(0.55, 0.55, 1.1, 16);
    const chamber = new THREE.Mesh(cGeo, mats.machineDark);
    chamber.castShadow = true;
    chamberGroup.add(chamber);

    const flangeGeo = new THREE.CylinderGeometry(0.58, 0.58, 0.08, 16);
    const flange = new THREE.Mesh(flangeGeo, mats.stainlessSteel);
    flange.position.y = 0.58;
    chamberGroup.add(flange);

    const pumpGeo = new THREE.CylinderGeometry(0.2, 0.2, 0.35, 12);
    const pump = new THREE.Mesh(pumpGeo, mats.stainlessSteel);
    pump.position.y = 0.8;
    chamberGroup.add(pump);

    // Glowing plasma inspection viewport (Cyan glow)
    const viewGeo = new THREE.CylinderGeometry(0.12, 0.12, 0.04, 12);
    const view = new THREE.Mesh(viewGeo, mats.waferDisc);
    view.rotation.x = Math.PI / 2;
    view.position.set(0, 0.2, 0.56);
    chamberGroup.add(view);

    etch.add(chamberGroup);
  }

  // 3. Equipment Front End Module (EFEM) with Dual Load Ports
  const efemGeo = new THREE.BoxGeometry(1.8, 1.2, 0.8);
  const efem = new THREE.Mesh(efemGeo, mats.cleanWhite);
  efem.position.set(0, 0.7, 1.7);
  efem.castShadow = true;
  etch.add(efem);

  for (const ex of [-0.45, 0.45]) {
    const portNest = new THREE.Mesh(new THREE.BoxGeometry(0.48, 0.5, 0.38), mats.machineDark);
    portNest.position.set(ex, 0.4, 2.15);
    etch.add(portNest);

    if (ex === 0.45) {
      const foup = createFoup(mats);
      foup.position.set(ex, 0.65, 2.15);
      etch.add(foup);
    }
  }

  return etch;
}

// ── Cleanroom Structural Pillars with Hazard Chevrons ─────────────────────────
function createCleanroomPillar(mats: Mats) {
  const group = new THREE.Group();

  const colGeo = new THREE.BoxGeometry(0.7, 8.0, 0.7);
  const col = new THREE.Mesh(colGeo, mats.cleanWhite);
  col.position.y = 4.0;
  col.castShadow = true;
  group.add(col);

  // Hazard base ring
  const baseGeo = new THREE.BoxGeometry(0.85, 0.6, 0.85);
  const base = new THREE.Mesh(baseGeo, mats.machineDark);
  base.position.y = 0.3;
  group.add(base);

  return group;
}

// ── Overhead Cleanroom Cable Ladder Tray ──────────────────────────────────────
function createCableTray(len: number, mats: Mats) {
  const tray = new THREE.Group();

  const beamGeo = new THREE.BoxGeometry(len, 0.04, 0.04);
  const railA = new THREE.Mesh(beamGeo, mats.machineDark);
  railA.position.set(0, 0, -0.2);
  tray.add(railA);

  const railB = new THREE.Mesh(beamGeo, mats.machineDark);
  railB.position.set(0, 0, 0.2);
  tray.add(railB);

  // Rungs
  const rungCount = Math.floor(len / 0.4);
  const rungGeo = new THREE.BoxGeometry(0.03, 0.02, 0.4);
  for (let i = 0; i <= rungCount; i++) {
    const rung = new THREE.Mesh(rungGeo, mats.machineDark);
    rung.position.set(-len / 2 + i * 0.4, 0, 0);
    tray.add(rung);
  }

  // Cyan fiber cable inside tray
  const cableGeo = new THREE.CylinderGeometry(0.025, 0.025, len, 8);
  const cable = new THREE.Mesh(cableGeo, mats.ledCyan);
  cable.rotation.z = Math.PI / 2;
  cable.position.set(0, 0.03, 0);
  tray.add(cable);

  return tray;
}

// ── Semiconductor AMR (Autonomous Mobile Robot) Model ─────────────────────────
function createCleanroomAmr(mats: Mats, hasFoup = true, statusColor: 'green' | 'amber' | 'cyan' = 'green') {
  const amr = new THREE.Group();

  // 1. Lower Chassis (Aerodynamic chamfered cleanroom skirt)
  const skirtGeo = new THREE.BoxGeometry(1.2, 0.16, 0.74);
  const skirt = new THREE.Mesh(skirtGeo, mats.amrBody);
  skirt.position.y = 0.12;
  skirt.castShadow = true;
  amr.add(skirt);

  // 2. Upper Body Deck (Pearl White cleanroom housing)
  const deckGeo = new THREE.BoxGeometry(1.08, 0.14, 0.68);
  const deck = new THREE.Mesh(deckGeo, mats.amrWhite);
  deck.position.y = 0.25;
  deck.castShadow = true;
  amr.add(deck);

  // 3. Safety LiDAR Bumpers (Front and Rear laser scanner pods)
  const lidarGeo = new THREE.CylinderGeometry(0.05, 0.05, 0.07, 12);
  const frontLidar = new THREE.Mesh(lidarGeo, mats.machineDark);
  frontLidar.position.set(0.61, 0.12, 0);
  amr.add(frontLidar);

  const rearLidar = new THREE.Mesh(lidarGeo, mats.machineDark);
  rearLidar.position.set(-0.61, 0.12, 0);
  amr.add(rearLidar);

  // 4. Perimeter Status Ring Light (360-degree LED band)
  const ledMat = statusColor === 'green' ? mats.ledGreen : statusColor === 'amber' ? mats.ledAmber : mats.ledCyan;
  const statusRingGeo = new THREE.BoxGeometry(1.18, 0.02, 0.72);
  const statusRing = new THREE.Mesh(statusRingGeo, ledMat);
  statusRing.position.y = 0.19;
  amr.add(statusRing);

  // 5. Four Drive/Caster Wheels
  const wheelPositions: Array<[number, number]> = [
    [-0.34, -0.36], [-0.34, 0.36],
    [0.34, -0.36],  [0.34, 0.36]
  ];
  const wheels: any[] = [];
  const tireGeo = new THREE.CylinderGeometry(0.09, 0.09, 0.065, 16);
  const hubGeo = new THREE.CylinderGeometry(0.055, 0.055, 0.07, 12);

  for (const [wx, wz] of wheelPositions) {
    const wheelGroup = new THREE.Group();
    const tire = new THREE.Mesh(tireGeo, mats.tire);
    tire.rotation.z = Math.PI / 2;
    tire.castShadow = true;
    wheelGroup.add(tire);

    const hub = new THREE.Mesh(hubGeo, mats.hub);
    hub.rotation.z = Math.PI / 2;
    wheelGroup.add(hub);

    wheelGroup.position.set(wx, 0.09, wz);
    amr.add(wheelGroup);
    wheels.push(wheelGroup);
  }
  amr.userData.wheels = wheels;

  // 6. Active Payload Bay: Carries a 300mm FOUP wafer pod
  if (hasFoup) {
    const foupNestGeo = new THREE.BoxGeometry(0.48, 0.04, 0.44);
    const foupNest = new THREE.Mesh(foupNestGeo, mats.machineDark);
    foupNest.position.y = 0.33;
    amr.add(foupNest);

    const foup = createFoup(mats);
    foup.position.set(0, 0.35, 0);
    amr.add(foup);
  }

  // 7. Ground ambient shadow & Headlight beam
  const shadowGeo = new THREE.PlaneGeometry(1.4, 0.9);
  const shadow = new THREE.Mesh(shadowGeo, mats.shadow);
  shadow.rotation.x = -Math.PI / 2;
  shadow.position.y = 0.005;
  amr.add(shadow);

  // Forward driving light cone projected on floor
  const lightConeGeo = new THREE.PlaneGeometry(1.2, 0.7);
  const lightCone = new THREE.Mesh(lightConeGeo, mats.headlightCone);
  lightCone.rotation.x = -Math.PI / 2;
  lightCone.position.set(1.1, 0.006, 0);
  amr.add(lightCone);

  return amr;
}

// ── 3D Billboard Sprite Generator for Floating AMR HUD Badges ─────────────────
function createAmrHudSprite(label: string, statusText: string, statusColor: string) {
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 140;
  const ctx = canvas.getContext('2d');
  if (!ctx) return new THREE.Sprite();

  // 1. Semi-transparent dark navy pill capsule
  ctx.save();
  const w = 490;
  const h = 110;
  const x = 11;
  const y = 15;
  const r = 24;

  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + w - r, y);
  ctx.quadraticCurveTo(x + w, y, x + w, y + r);
  ctx.lineTo(x + w, y + h - r);
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  ctx.lineTo(x + r, y + h);
  ctx.quadraticCurveTo(x, y + h, x, y + h - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();

  ctx.fillStyle = 'rgba(6, 18, 34, 0.90)';
  ctx.fill();

  // Cyan neon border
  ctx.strokeStyle = 'rgba(0, 229, 255, 0.85)';
  ctx.lineWidth = 3.5;
  ctx.stroke();

  // 2. Status Dot
  ctx.beginPath();
  ctx.arc(x + 40, y + h / 2, 11, 0, Math.PI * 2);
  ctx.fillStyle = statusColor;
  ctx.fill();

  // Status Dot Glow Ring
  ctx.beginPath();
  ctx.arc(x + 40, y + h / 2, 18, 0, Math.PI * 2);
  ctx.strokeStyle = statusColor;
  ctx.lineWidth = 2.5;
  ctx.stroke();

  // 3. AMR Label
  ctx.fillStyle = '#ffffff';
  ctx.font = 'bold 36px "IBM Plex Mono", monospace';
  ctx.textBaseline = 'middle';
  ctx.fillText(label, x + 72, y + h / 2);

  // 4. Status Text
  ctx.fillStyle = statusColor;
  ctx.font = 'bold 32px "Noto Sans SC", sans-serif';
  ctx.fillText(statusText, x + 310, y + h / 2);
  ctx.restore();

  const texture = new THREE.CanvasTexture(canvas);
  texture.minFilter = THREE.LinearFilter;
  const spriteMat = new THREE.SpriteMaterial({
    map: texture,
    transparent: true,
    depthTest: false
  });
  const sprite = new THREE.Sprite(spriteMat);
  sprite.scale.set(1.9, 0.52, 1);
  return sprite;
}

// ── 3D Billboard Sprite Generator for Floating Zone Titles ─────────────────────
function createZoneTitleSprite(mainTitle: string, subTitle: string) {
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 220;
  const ctx = canvas.getContext('2d');
  if (!ctx) return new THREE.Sprite();

  ctx.save();
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';

  // English Major Title (e.g. LITHOGRAPHY)
  ctx.fillStyle = 'rgba(235, 246, 255, 0.95)';
  ctx.font = 'bold 44px "IBM Plex Sans", sans-serif';
  ctx.fillText(mainTitle, 256, 80);

  // Chinese Subtitle (e.g. 光刻区)
  ctx.fillStyle = '#00e5ff';
  ctx.font = 'bold 36px "Noto Sans SC", sans-serif';
  ctx.fillText(subTitle, 256, 145);

  ctx.restore();

  const texture = new THREE.CanvasTexture(canvas);
  texture.minFilter = THREE.LinearFilter;
  const spriteMat = new THREE.SpriteMaterial({
    map: texture,
    transparent: true,
    depthTest: false
  });
  const sprite = new THREE.Sprite(spriteMat);
  sprite.scale.set(3.4, 1.5, 1);
  return sprite;
}

// ── Navigation Laser Network (Orthogonal Highway with Fillets) ─────────────────
function createLaserPathMesh(points: any[], mats: Mats) {
  const curve = new THREE.CatmullRomCurve3(points, false, 'catmullrom', 0.05);

  // Outer glow ribbon
  const glowGeo = new THREE.TubeGeometry(curve, 160, 0.24, 8, false);
  const glow = new THREE.Mesh(glowGeo, mats.pathGlow);
  glow.position.y = 0.035;

  // Center bright laser core
  const coreGeo = new THREE.TubeGeometry(curve, 160, 0.048, 8, false);
  const core = new THREE.Mesh(coreGeo, mats.pathCore);
  core.position.y = 0.038;

  const group = new THREE.Group();
  group.add(glow);
  group.add(core);
  return { group, curve };
}

// ── Main Scene Runtime ────────────────────────────────────────────────────────
export function createFactoryYardRuntime(host: HTMLElement, _initialDark = true) {
  const mats = createMaterials();
  const scene = new THREE.Scene();
  scene.background = null;

  // 1. Camera: Elevated cleanroom isometric overview (38° high angle)
  const baseCamPos = new THREE.Vector3(13.8, 13.5, 15.5);
  const baseLookAt = new THREE.Vector3(-0.6, 0.5, -0.8);
  const camera = new THREE.PerspectiveCamera(36, 1, 0.1, 100);
  camera.position.copy(baseCamPos);
  camera.lookAt(baseLookAt);

  let targetParallaxX = 0;
  let targetParallaxY = 0;
  let curParallaxX = 0;
  let curParallaxY = 0;

  // 2. High-Performance WebGL Renderer (Transparent overlay on top of photorealistic cleanroom fab)
  const renderer = new THREE.WebGLRenderer({
    antialias: true,
    alpha: true,
    powerPreference: 'high-performance'
  });
  renderer.setClearColor(0x000000, 0);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2.0));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.35;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.domElement.style.width = '100%';
  renderer.domElement.style.height = '100%';
  renderer.domElement.style.display = 'block';
  host.appendChild(renderer.domElement);

  // 3. Cleanroom Lighting (Crisp 5500K ceiling array + cyan accent lights)
  const hemi = new THREE.HemisphereLight(0xd5e8fa, 0x182638, 1.2);
  scene.add(hemi);

  // Overhead cleanroom ceiling FFU key light
  const cleanSun = new THREE.DirectionalLight(0xffffff, 1.8);
  cleanSun.position.set(10, 24, 12);
  cleanSun.castShadow = true;
  scene.add(cleanSun);

  // Local cyan accent lights on paths and stocker
  const stockerLight = new THREE.PointLight(0x00e5ff, 2.8, 16, 2);
  stockerLight.position.set(-2.5, 2.4, 5.8);
  scene.add(stockerLight);

  const junctionLight = new THREE.PointLight(0x00e5ff, 2.2, 14, 2);
  junctionLight.position.set(0.5, 1.8, 0.5);
  scene.add(junctionLight);

  const chargingLight = new THREE.PointLight(0xf0b429, 2.4, 10, 2);
  chargingLight.position.set(3.8, 1.5, 2.8);
  scene.add(chargingLight);

  // 4. Ground Floor (Transparent shadow catcher)
  const floorMat = new THREE.ShadowMaterial({ opacity: 0.18 });
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(64, 54), floorMat);
  floor.rotation.x = -Math.PI / 2;
  floor.receiveShadow = true;
  scene.add(floor);

  // 5. Cleanroom Floating Optical Motes (microscopic particles drifting in cleanroom illumination)
  const moteCount = 50;
  const moteGeo = new THREE.BufferGeometry();
  const motePositions = new Float32Array(moteCount * 3);
  const moteVelocities: { x: number; y: number; z: number }[] = [];
  for (let i = 0; i < moteCount; i++) {
    motePositions[i * 3] = (Math.random() - 0.5) * 24;
    motePositions[i * 3 + 1] = Math.random() * 8 + 0.4;
    motePositions[i * 3 + 2] = (Math.random() - 0.5) * 24;
    moteVelocities.push({
      x: (Math.random() - 0.5) * 0.006,
      y: Math.random() * 0.004 + 0.001,
      z: (Math.random() - 0.5) * 0.006
    });
  }
  moteGeo.setAttribute('position', new THREE.BufferAttribute(motePositions, 3));
  const moteMat = new THREE.PointsMaterial({
    color: 0x88e5ff,
    size: 0.14,
    transparent: true,
    opacity: 0.6,
    blending: THREE.AdditiveBlending,
    depthWrite: false
  });
  const motes = new THREE.Points(moteGeo, moteMat);
  scene.add(motes);

  // 9. Animation & Render Loop
  const clock = new THREE.Clock();
  let raf = 0;

  const resize = () => {
    const w = host.clientWidth;
    const h = host.clientHeight;
    if (w < 2 || h < 2) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
  };
  const ro = new ResizeObserver(resize);
  ro.observe(host);
  resize();

  const tick = () => {
    raf = requestAnimationFrame(tick);
    const dt = Math.min(clock.getDelta(), 0.05);
    const t = clock.elapsedTime;

    // Smooth camera mouse parallax
    curParallaxX += (targetParallaxX - curParallaxX) * 0.05;
    curParallaxY += (targetParallaxY - curParallaxY) * 0.05;
    camera.position.x = baseCamPos.x + curParallaxX * 0.6;
    camera.position.y = baseCamPos.y - curParallaxY * 0.4;
    camera.lookAt(baseLookAt.x + curParallaxX * 0.2, baseLookAt.y, baseLookAt.z);

    // Dynamic lighting subtle breathing
    junctionLight.intensity = 2.0 + Math.sin(t * 2.0) * 0.4;
    chargingLight.intensity = 2.2 + Math.cos(t * 1.8) * 0.3;

    // Drifting cleanroom optical motes
    const mPos = moteGeo.attributes.position.array as Float32Array;
    for (let i = 0; i < moteCount; i++) {
      mPos[i * 3] += moteVelocities[i].x;
      mPos[i * 3 + 1] += moteVelocities[i].y;
      mPos[i * 3 + 2] += moteVelocities[i].z;
      if (mPos[i * 3 + 1] > 8.5) mPos[i * 3 + 1] = 0.4;
      if (mPos[i * 3] > 12) mPos[i * 3] = -12;
      if (mPos[i * 3] < -12) mPos[i * 3] = 12;
      if (mPos[i * 3 + 2] > 12) mPos[i * 3 + 2] = -12;
      if (mPos[i * 3 + 2] < -12) mPos[i * 3 + 2] = 12;
    }
    moteGeo.attributes.position.needsUpdate = true;

    renderer.render(scene, camera);
  };
  tick();

  return {
    setTheme(_dark: boolean) {},
    setMouseParallax(normX: number, normY: number) {
      targetParallaxX = normX;
      targetParallaxY = normY;
    },
    dispose() {
      cancelAnimationFrame(raf);
      ro.disconnect();
      disposeObject(scene);
      renderer.dispose();
      renderer.domElement.remove();
    }
  };
}
