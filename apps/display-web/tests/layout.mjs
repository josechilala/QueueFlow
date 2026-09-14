import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { pathToFileURL, fileURLToPath } from 'node:url';

// Run after next build. Playwright can live outside the project's dependencies.
const { chromium } = await import(process.env.QUEUEFLOW_PLAYWRIGHT_MODULE ? pathToFileURL(process.env.QUEUEFLOW_PLAYWRIGHT_MODULE).href : 'playwright');
const appDir = fileURLToPath(new URL('../', import.meta.url));
const fixture = createServer((req, res) => {
  const empty = req.url.includes('empty');
  const long = req.url.includes('long');
  res.setHeader('Content-Type', 'application/json');
  res.end(JSON.stringify({ branchPublicId: 'fixture', organizationName: long ? 'Organizacao de atendimento e servicos especializados' : 'QueueFlow', branchName: long ? 'Unidade central de atendimento ao cliente' : 'Salao', queuePublicIds: [], latestCalls: empty ? [] : Array.from({ length: 10 }, (_, i) => ({ ticketId: `ticket-${i}`, queueId: 'queue', ticketNumber: long ? `ESTETICA-${100 + i}` : `A-${100 + i}`, counterName: long ? 'Atendimento especializado 12' : '12', calledAt: new Date(Date.now() - i * 1000).toISOString() })) }));
});
fixture.listen(0, '127.0.0.1'); await once(fixture, 'listening');
const port = 3202;
const child = spawn(process.execPath, ['../../node_modules/next/dist/bin/next', 'start', '-p', `${port}`], { cwd: appDir, env: { ...process.env, QUEUEFLOW_API_URL: `http://127.0.0.1:${fixture.address().port}` }, stdio: ['ignore', 'pipe', 'pipe'] });
let logs = ''; child.stdout.on('data', x => { logs += x; }); child.stderr.on('data', x => { logs += x; });
let browser;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`http://localhost:${port}`)).ok) { ready = true; break; } } catch {}
    await new Promise(r => setTimeout(r, 100));
  }
  assert.ok(ready, logs);
  browser = await chromium.launch({ headless: true, channel: 'chromium' });
  for (const [width, height, maximum] of [[1920,1080,5], [1600,900,4], [1366,768,3], [1280,720,3], [1024,768,3]]) {
    const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
    for (const scenario of ['normal', 'long', 'empty']) {
      await page.goto(`http://localhost:${port}/display/${scenario}`);
      await page.evaluate(() => document.fonts.ready);
      const result = await page.evaluate(() => {
        const root = document.documentElement;
        const visible = [...document.querySelectorAll('main, header, section, article, aside, h1, h2, h3, small, p, b, span, i')].filter(el => el.getClientRects().length);
        const outside = visible.filter(el => { const r = el.getBoundingClientRect(); return r.left < -.5 || r.top < -.5 || r.right > innerWidth + .5 || r.bottom > innerHeight + .5; }).map(el => el.tagName + ':' + el.textContent);
        const overflow = visible.filter(el => el.scrollHeight > el.clientHeight + 1 && el.clientHeight > 0 || el.scrollWidth > el.clientWidth + 1 && el.clientWidth > 0).map(el => ({ tag: el.tagName, text: el.textContent, sh: el.scrollHeight, ch: el.clientHeight, sw: el.scrollWidth, cw: el.clientWidth }));
        const current = document.querySelector('.current-call').getBoundingClientRect();
        const history = document.querySelector('.call-history').getBoundingClientRect();
        return { scrollHeight: root.scrollHeight, clientHeight: root.clientHeight, scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, bodyHeight: document.body.scrollHeight, outside, overflow, aligned: Math.abs(current.bottom - history.bottom) < 1, calls: [...document.querySelectorAll('.call-history p')].filter(el => el.getClientRects().length).length, zoom: visualViewport.scale };
      });
      assert.equal(result.zoom, 1);
      assert.ok(result.scrollHeight <= result.clientHeight && result.bodyHeight <= height, JSON.stringify(result));
      assert.ok(result.scrollWidth <= result.clientWidth, JSON.stringify(result));
      assert.deepEqual(result.outside, [], `${width}x${height} ${scenario}`);
      assert.deepEqual(result.overflow, [], `${width}x${height} ${scenario}`);
      assert.ok(result.aligned);
      assert.equal(result.calls, scenario === 'empty' ? 1 : maximum);
      console.log(JSON.stringify({ width, height, scenario, ...result }));
    }
    await page.close();
  }
} finally {
  await browser?.close(); child.kill(); fixture.close();
}
