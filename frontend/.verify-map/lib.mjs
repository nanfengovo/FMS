import puppeteer from 'puppeteer-core';
import fs from 'node:fs';

export const BASE = 'http://localhost:9527';
export const OUT = '/tmp/mapdrv/out';
fs.mkdirSync(OUT, { recursive: true });

export async function launch({ dpr = 2, width = 1440, height = 900, dark = false } = {}) {
  const browser = await puppeteer.launch({
    executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    headless: true,
    userDataDir: '/tmp/mapdrv/profile',
    args: [
      '--no-sandbox',
      '--use-gl=angle',
      '--use-angle=swiftshader',
      '--enable-unsafe-swiftshader',
      '--ignore-gpu-blocklist',
      '--disable-features=Translate',
      `--window-size=${width},${height}`
    ],
    protocolTimeout: 40000,
    defaultViewport: { width, height, deviceScaleFactor: dpr }
  });
  const page = (await browser.pages())[0] || (await browser.newPage());
  const logs = [];
  page.on('console', msg => {
    const t = msg.type();
    if (t === 'error' || t === 'warning') logs.push(`[${t}] ${msg.text().slice(0, 300)}`);
  });
  page.on('pageerror', err => logs.push(`[pageerror] ${String(err).slice(0, 400)}`));
  page.on('dialog', async d => {
    logs.push(`[dialog] ${d.type()}: ${d.message().slice(0, 200)}`);
    try { await d.dismiss(); } catch {}
  });
  page.on('error', err => logs.push(`[crash] ${String(err).slice(0, 200)}`));
  return { browser, page, logs };
}

export async function ensureLogin(page) {
  await page.goto(`${BASE}/fms/screen`, { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 2500));
  if (page.url().includes('/login')) {
    // The login page offers one-click demo accounts; use the admin one.
    const buttons = await page.$$('button');
    for (const b of buttons) {
      const text = await page.evaluate(el => el.innerText, b);
      if (text.includes('管理员')) {
        await b.click();
        break;
      }
    }
    await page.waitForFunction(() => !location.pathname.includes('/login'), { timeout: 15000 });
    await new Promise(r => setTimeout(r, 1500));
  }
}

export async function setDark(page, dark) {
  await page.evaluate(d => {
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia;
    const theme = pinia._s.get('theme-store');
    theme.setThemeScheme(d ? 'dark' : 'light');
  }, dark);
  await new Promise(r => setTimeout(r, 400));
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));
