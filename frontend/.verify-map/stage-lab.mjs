import { launch, ensureLogin, setDark, sleep, OUT, BASE } from './lib.mjs';

/** Mount the in-development map component over the logged-in app so it receives live store data. */
export async function mountStage(page, { component = 'MapStage', props = {} } = {}) {
  await page.goto(`${BASE}/home`, { waitUntil: 'domcontentloaded' });
  await sleep(4000);
  const result = await page.evaluate(
    async (component, extraProps) => {
      const vueUrl = performance
        .getEntriesByType('resource')
        .map(r => r.name)
        .find(n => /deps\/vue\.js/.test(n));
      const Vue = await import(vueUrl);
      const mod = await import(`/src/components/fms/${component}.vue`);
      const Stage = mod.default;
      const app = document.querySelector('#app').__vue_app__;
      const pinia = app.config.globalProperties.$pinia;
      const mapStore = pinia._s.get('map');
      const simStore = pinia._s.get('simulation');
      await mapStore.loadCurrentMap();
      await simStore.init();
      const holder = document.createElement('div');
      holder.id = '__stage_holder';
      holder.style.cssText = 'position:fixed;left:0;top:0;width:100vw;height:100vh;z-index:99999;background:#fff';
      document.body.appendChild(holder);
      const root = {
        setup() {
          return () =>
            Vue.h(Stage, {
              ref: r => {
                window.__stage = r;
              },
              graph: mapStore.currentGraph,
              vehicles: simStore.vehicles,
              orders: simStore.liveOrders,
              conflicts: simStore.activeConflicts,
              mutexZones: simStore.mutexZones,
              pointCloud: mapStore.pointCloud,
              raster: mapStore.raster,
              layerVisibility: mapStore.layerVisibility,
              selectedVehicleId: simStore.selectedVehicleId,
              showSearch: true,
              showNavigator: true,
              showToolbar: true,
              showCruise: true,
              deselectable: true,
              enableTaskMenu: true,
              onSelectVehicle: id => {
                simStore.selectedVehicleId = id;
              },
              onRequestCreateTask: p => {
                window.__lastCreate = p;
              },
              ...extraProps
            });
        }
      };
      const sub = Vue.createApp(root);
      sub.use(pinia);
      sub.mount(holder);
      window.__sub = sub;
      return { nodes: mapStore.currentGraph?.nodes.length, vehicles: simStore.vehicles.length };
    },
    component,
    props
  );
  await sleep(2500);
  return result;
}

export async function flightDone(page, timeout = 6000) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    await sleep(120);
    const cam = await page.evaluate(() => window.__stage?.getCamera());
    if (!cam) continue;
    await sleep(200);
    const cam2 = await page.evaluate(() => window.__stage?.getCamera());
    if (cam2 && Math.abs(cam.x - cam2.x) < 0.01 && Math.abs(cam.y - cam2.y) < 0.01 && Math.abs(cam.scale - cam2.scale) < 0.01) return true;
  }
  return false;
}

export async function shot(page, name, clip) {
  const path = `${OUT}/${name}.png`;
  await page.screenshot({ path, ...(clip ? { clip } : {}) });
  return path;
}

export { launch, ensureLogin, setDark, sleep, OUT, BASE };
