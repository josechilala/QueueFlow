import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { fileURLToPath, pathToFileURL } from 'node:url';

const playwrightPath = process.env.QUEUEFLOW_PLAYWRIGHT_MODULE;
const { chromium } = await import(playwrightPath ? pathToFileURL(playwrightPath).href : 'playwright');
const appDirectory = fileURLToPath(new URL('../', import.meta.url));
const branchId = '0123456789abcdef0123456789abcdef';

const api = createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.url === '/api/v1/auth/me') return response.end(JSON.stringify({ userId: 'user', organizationId: 'organization', name: 'Owner', email: 'owner@test.local', role: 'Owner' }));
  if (request.url === '/api/v1/dashboard') return response.end(JSON.stringify({ organizationSlug: 'empresa-teste', inService: 0, appointmentsToday: 0, upcomingAppointments: 0, activeBranches: 0, queues: [], activeQueues: 0, waiting: 0, completedToday: 0, averageWaitMinutes: 0, generatedAt: new Date().toISOString(), queuesInProgress: [] }));
  if (request.url === '/api/v1/branches/branch-record') return response.end(JSON.stringify({ id: 'branch-record', publicId: branchId, name: 'Unidade Centro', address: null, timeZone: 'UTC', isActive: true }));
  response.statusCode = 404;
  response.end('{}');
});

api.listen(0, '127.0.0.1');
await once(api, 'listening');
const appPort = 3210;
const app = spawn(process.execPath, ['../../node_modules/next/dist/bin/next', 'start', '-p', String(appPort)], {
  cwd: appDirectory,
  env: {
    ...process.env,
    QUEUEFLOW_API_URL: `http://127.0.0.1:${api.address().port}`,
    QUEUEFLOW_PUBLIC_URL: 'https://admin.queueflow.test/',
    QUEUEFLOW_CUSTOMER_URL: 'https://customer.queueflow.test/',
    NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL: 'https://attendant.queueflow.test/',
    NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL: 'https://display.queueflow.test/',
  },
  stdio: ['ignore', 'pipe', 'pipe'],
});

let output = '';
app.stdout.on('data', value => { output += value; });
app.stderr.on('data', value => { output += value; });
let browser;
try {
  let ready = false;
  for (let attempt = 0; attempt < 200; attempt++) {
    try {
      if ((await fetch(`http://localhost:${appPort}/login`)).ok) { ready = true; break; }
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.ok(ready, output);

  browser = await chromium.launch({ headless: true, channel: 'chromium' });
  const context = await browser.newContext({ permissions: ['clipboard-read', 'clipboard-write'] });
  await context.addCookies([{ name: 'queueflow_access', value: 'test-token', url: `http://localhost:${appPort}` }]);
  const page = await context.newPage();

  await page.goto(`http://localhost:${appPort}/dashboard`);
  assert.equal(await page.locator('.access-link-card').count(), 2);
  await page.locator('[data-link-id="scheduling"] a', { hasText: 'Abrir' }).evaluate(element => {
    if (element.getAttribute('target') !== '_blank' || element.getAttribute('rel') !== 'noopener noreferrer') throw new Error('Unsafe open link');
  });
  assert.equal(await page.locator('[data-link-id="scheduling"] code').textContent(), 'https://customer.queueflow.test/agendamento/empresa-teste');

  await page.goto(`http://localhost:${appPort}/branches/branch-record`);
  const expected = {
    attendant: 'https://attendant.queueflow.test',
    display: `https://display.queueflow.test/display/${branchId}`,
    customer: `https://customer.queueflow.test/unidade/${branchId}`,
    scheduling: 'https://customer.queueflow.test/agendamento/empresa-teste',
  };
  for (const [id, url] of Object.entries(expected)) {
    const card = page.locator(`[data-link-id="${id}"]`);
    assert.equal(await card.locator('code').textContent(), url);
    assert.equal(await card.locator('a', { hasText: 'Abrir' }).getAttribute('href'), url);
    await card.getByRole('button', { name: 'Copiar link' }).click();
    assert.equal(await page.evaluate(() => navigator.clipboard.readText()), url);
    await card.getByRole('button', { name: 'Link copiado' }).waitFor();
  }
  const customer = page.locator('[data-link-id="customer"]');
  await customer.getByRole('button', { name: 'Mostrar QR Code' }).click();
  assert.equal(await customer.locator('.access-link-qr svg').count(), 1);

  for (const viewport of [{ width: 1280, height: 800 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport);
    const layout = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      outside: [...document.querySelectorAll('.access-link-card')].some(element => {
        const rectangle = element.getBoundingClientRect();
        return rectangle.left < 0 || rectangle.right > innerWidth + 0.5;
      }),
      columns: getComputedStyle(document.querySelector('.access-link-grid')).gridTemplateColumns.split(' ').length,
    }));
    assert.ok(layout.scrollWidth <= layout.clientWidth, JSON.stringify({ viewport, layout }));
    assert.equal(layout.outside, false);
    assert.equal(layout.columns, viewport.width <= 760 ? 1 : 2);
  }
  console.log('PASS: access links, exact IDs, safe open, clipboard, QR, desktop/tablet/mobile layout');
} finally {
  await browser?.close();
  app.kill();
  api.close();
}
