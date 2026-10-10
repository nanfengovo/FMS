import { launch, ensureLogin, sleep, mountStage } from './stage-lab.mjs';

const { browser, page, logs } = await launch({});
try {
  await ensureLogin(page);
  const info = await mountStage(page);
  console.log('mounted', JSON.stringify(info));
  const client = await page.createCDPSession();
  await client.send('Debugger.enable');
  const paused = new Promise(resolve => client.once('Debugger.paused', resolve));
  await sleep(4000);
  await client.send('Debugger.pause');
  const ev = await Promise.race([paused, sleep(15000).then(() => null)]);
  if (!ev) {
    console.log('no pause event (page not executing JS: likely waiting on GPU/render)');
  } else {
    for (const f of ev.callFrames.slice(0, 14)) {
      console.log(`${f.functionName || '(anon)'}  ${f.url.split('/').slice(-2).join('/')}:${f.location.lineNumber + 1}`);
    }
  }
} catch (e) {
  console.log('ERR', e.stack || e.message);
} finally {
  console.log(logs.slice(0, 20).join('\n'));
  await browser.close();
}
