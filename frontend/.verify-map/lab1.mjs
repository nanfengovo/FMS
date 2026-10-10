import { launch, ensureLogin, setDark, sleep, mountStage, flightDone, shot } from './stage-lab.mjs';

const dark = process.argv[2] === 'dark';
const { browser, page, logs } = await launch({});
try {
  await ensureLogin(page);
  const info = await mountStage(page);
  console.log('mounted', JSON.stringify(info));
  await setDark(page, dark);
  await sleep(1500);
  console.log('cam', JSON.stringify(await page.evaluate(() => window.__stage.getCamera())));
  await shot(page, `lab-fit-${dark ? 'dark' : 'light'}`);
} catch (e) {
  console.log('ERR', e.stack || e.message);
} finally {
  console.log(logs.slice(0, 20).join('\n'));
  await browser.close();
}
