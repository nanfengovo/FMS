import { launch, ensureLogin, sleep, BASE } from './stage-lab.mjs';

const { browser, page, logs } = await launch({});
try {
  await ensureLogin(page);
  await page.goto(`${BASE}/home`, { waitUntil: 'domcontentloaded' });
  await sleep(3000);
  const info = await page.evaluate(async () => {
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia;
    const mapStore = pinia._s.get('map');
    await mapStore.loadCurrentMap();
    const r = mapStore.raster;
    if (!r) return { raster: null, pointCloud: mapStore.pointCloud.length };
    const img = new Image();
    img.src = r.url;
    await img.decode();
    return {
      w: img.naturalWidth,
      h: img.naturalHeight,
      meters: [r.widthMeters, r.heightMeters],
      origin: [r.originX, r.topY],
      pointCloud: mapStore.pointCloud.length,
      bounds: mapStore.currentGraph.bounds
    };
  });
  console.log(JSON.stringify(info));
} catch (e) {
  console.log('ERR', e.message);
} finally {
  console.log(logs.slice(0, 5).join('\n'));
  await browser.close();
}
