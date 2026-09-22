import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';

const origin = process.env.MARKETING_TEST_ORIGIN || 'http://127.0.0.1:3004';
const output = resolve('apps/marketing-web/.next/visual-check');
await mkdir(output, { recursive: true });
const chrome = spawn(process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=9334', `--user-data-dir=${resolve(output, 'profile')}`, 'about:blank',
], { windowsHide: true, stdio: 'ignore' });
let socket;
try {
  let tabs;
  for (let i = 0; i < 60; i++) {
    try { tabs = await (await fetch('http://127.0.0.1:9334/json')).json(); break; } catch { await new Promise(r => setTimeout(r, 250)); }
  }
  assert.ok(tabs, 'Chrome debugging endpoint available');
  socket = new WebSocket(tabs.find(tab => tab.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let id = 0;
  const pending = new Map();
  socket.onmessage = event => { const message = JSON.parse(event.data); if (message.id) { const item = pending.get(message.id); pending.delete(message.id); message.error ? item.reject(message.error) : item.resolve(message.result); } };
  const send = (method, params = {}) => new Promise((resolve, reject) => { const key = ++id; pending.set(key, { resolve, reject }); socket.send(JSON.stringify({ id: key, method, params })); });
  const evaluate = async expression => (await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })).result.value;
  await send('Page.enable');
  await send('Runtime.enable');
  const results = [];
  for (const width of [1440, 1024, 768, 390, 320]) {
    await send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
    await send('Page.navigate', { url: origin + '/' });
    for (let i = 0; i < 60; i++) { if (await evaluate('document.readyState === "complete" && !!document.querySelector(".hero")')) break; await new Promise(r => setTimeout(r, 100)); }
    await evaluate('document.fonts.ready.then(() => true)');
    const result = await evaluate(`({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth, title: document.title, h1: document.querySelectorAll('h1').length, missingAnchors: [...document.querySelectorAll('a[href^="#"], a[href^="/#"]')].map(a => a.hash.slice(1)).filter(id => !document.getElementById(id)), contact: document.querySelector('#contato').innerText, externalResources: performance.getEntriesByType('resource').filter(r => !r.name.startsWith(location.origin)).map(r => r.name) })`);
    assert.ok(result.scrollWidth <= width, `No horizontal overflow at ${width}: ${result.scrollWidth}`);
    assert.equal(result.h1, 1);
    assert.deepEqual(result.missingAnchors, []);
    assert.deepEqual(result.externalResources, []);
    assert.ok(result.contact.includes('Solicitar teste grátis'));
    assert.equal(await evaluate(`(() => { const faq = document.querySelector('.faq-list details'); faq.querySelector('summary').click(); return faq.open; })()`), true);
    if (width <= 900) assert.equal(await evaluate(`(() => { const menu = document.querySelector('.mobile-menu'); menu.querySelector('summary').click(); const ok = menu.open && menu.getBoundingClientRect().width > 0; menu.open = false; return ok; })()`), true);
    for (let i = 0; i < 60; i++) { if (await evaluate('!!document.querySelector(".demo-pause")')) break; await new Promise(r => setTimeout(r, 100)); }
    for (let slide = 0; slide < 5; slide++) {
      await evaluate(`document.querySelectorAll('.demo-dots button')[${slide}].click()`);
      await new Promise(r => setTimeout(r, 100));
      assert.equal(await evaluate(`document.querySelector('.demo-slide.is-active').getAttribute('aria-label').startsWith('${slide + 1} de 5')`), true);
      assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'));
    }
    await evaluate("document.querySelector('.demo-dots button').click()");
    await new Promise(r => setTimeout(r, 400));
    const metrics = await send('Page.getLayoutMetrics');
    const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip: { x: 0, y: 0, width, height: metrics.cssContentSize.height, scale: 1 } });
    await writeFile(resolve(output, `${width}.png`), Buffer.from(shot.data, 'base64'));
    const viewportShot = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width, height: 1000, scale: 1 } });
    await writeFile(resolve(output, `${width}-hero.png`), Buffer.from(viewportShot.data, 'base64'));
    results.push(result);
  }
  for (const path of ['termos', 'privacidade']) {
    const response = await fetch(`${origin}/${path}/`);
    assert.equal(response.status, 200);
    const html = await response.text();
    assert.ok(html.includes('Minuta'));
    assert.ok(html.includes('noindex'));
  }
  await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
  await new Promise(r => setTimeout(r, 100));
  assert.equal(await evaluate('document.querySelector(".demo-pause") === null'), true);
  assert.equal(await evaluate('getComputedStyle(document.querySelector(".demo-slide.is-active")).animationName'), 'none');
  console.log('PASS: five slides at 1440, 1024, 768, 390 and 320px; navigation, reduced motion, static legal pages, no external resources.');
  await send('Browser.close');
} finally { socket?.close(); chrome.kill(); }
