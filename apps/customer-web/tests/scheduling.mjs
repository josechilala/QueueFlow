import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { pathToFileURL, fileURLToPath } from 'node:url';
const { chromium } = await import(process.env.QUEUEFLOW_PLAYWRIGHT_MODULE ? pathToFileURL(process.env.QUEUEFLOW_PLAYWRIGHT_MODULE).href : 'playwright');
const appDir = fileURLToPath(new URL('../', import.meta.url));
const services = [{ publicId: 'appointment', name: 'Appointment service', attendanceMode: 'AppointmentOnly', canSchedule: true }, { publicId: 'hybrid', name: 'Hybrid service', attendanceMode: 'Hybrid', canSchedule: true }];
const fixture = createServer((req, res) => {
  res.setHeader('Content-Type', 'application/json');
  if (req.url.includes('/availability')) {
    const date = new URL(req.url, 'http://localhost').searchParams.get('date');
    return res.end(JSON.stringify({ branchPublicId: 'b0', servicePublicId: 'hybrid', organizationName: 'Company', branchName: 'Branch 0', serviceName: 'Hybrid service', date, timeZone: 'America/Sao_Paulo', minimumDate: '2026-01-01', maximumDate: '2027-12-31', availableDaysOfWeek: ['Tuesday', 'Wednesday'], slots: [{ startAt: `${date}T13:00:00Z`, endAt: `${date}T13:30:00Z`, remainingCapacity: 1 }] }));
  }
  const slug = req.url.includes('/multi/') ? 'multi' : 'single';
  res.end(JSON.stringify({ slug, name: 'Company', branches: Array.from({ length: slug === 'multi' ? 3 : 1 }, (_, i) => ({ publicId: `b${i}`, name: `Branch ${i}`, services })) }));
});
fixture.listen(0, '127.0.0.1'); await once(fixture, 'listening');
const port = 3201;
const child = spawn(process.execPath, ['../../node_modules/next/dist/bin/next', 'start', '-p', `${port}`], { cwd: appDir, env: { ...process.env, QUEUEFLOW_API_URL: `http://127.0.0.1:${fixture.address().port}` }, stdio: ['ignore', 'pipe', 'pipe'] });
let logs = ''; child.stdout.on('data', x => { logs += x; }); child.stderr.on('data', x => { logs += x; });
let browser;
try {
  let ready = false;
  for (let i = 0; i < 200; i++) { try { if ((await fetch(`http://localhost:${port}`)).ok) { ready = true; break; } } catch {} await new Promise(r => setTimeout(r, 100)); }
  assert.ok(ready, logs);
  browser = await chromium.launch({ headless: true, channel: 'chromium' }); const page = await browser.newPage(); page.setDefaultTimeout(60000);
  const noQueueLinks = async () => assert.equal(await page.locator('a[href^="/q/"], a[href^="/unidade/"]').count(), 0);
  await page.goto(`http://localhost:${port}/agendamento/single`);
  assert.equal(await page.getByText('Selecionar unidade', { exact: true }).count(), 0);
  await page.getByRole('link', { name: 'Agendar hor', exact: false }).first().waitFor(); assert.equal(await page.getByRole('link', { name: 'Agendar hor', exact: false }).count(), 2); await noQueueLinks();
  await page.goto(`http://localhost:${port}/agendamento/multi`);
  assert.equal(await page.getByRole('link', { name: 'Selecionar unidade' }).count(), 3);
  await page.getByRole('link', { name: 'Selecionar unidade' }).first().click();
  await page.getByRole('link', { name: 'Agendar hor', exact: false }).first().waitFor(); assert.equal(await page.getByRole('link', { name: 'Agendar hor', exact: false }).count(), 2); await noQueueLinks();
  await page.locator('a[href$="/b0/hybrid"]').click();
  await page.getByRole('heading', { name: 'Hybrid service' }).waitFor(); await noQueueLinks();
  assert.ok(await page.locator('a[href="/agendamento/multi?unidade=b0"]').count());
  await page.locator('input[name="date"]').fill('2026-09-23');
  await page.getByRole('button', { name: 'Consultar', exact: true }).click();
  await page.waitForURL('**date=2026-09-23*');
  await page.locator('.slot').first().click();
  await page.getByRole('button', { name: 'Continuar', exact: true }).click();
  assert.ok((await page.locator('.booking-review').innerText()).includes('23 de setembro de 2026'));
  await page.locator('input[name="date"]').fill('2026-09-22');
  assert.equal(await page.locator('.slot, .booking-review, input[name="scheduledStart"]').count(), 0);
  await page.getByRole('button', { name: 'Consultar', exact: true }).click();
  await page.waitForURL('**date=2026-09-22*');
  await page.locator('.slot').first().click();
  let booking;
  await page.route('**/api/appointments', async route => { booking = route.request().postDataJSON(); await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ publicToken: 'confirmation' }) }); });
  await page.route('**/meu-agendamento/confirmation', route => route.fulfill({ contentType: 'text/html', body: '<h1>Confirmed</h1>' }));
  await page.locator('input[name="customerName"]').fill('Test Customer');
  await page.locator('input[name="customerEmail"]').fill('customer@example.test');
  await page.getByRole('button', { name: 'Continuar', exact: true }).click();
  assert.ok((await page.locator('.booking-review').innerText()).includes('22 de setembro de 2026'));
  assert.ok((await page.locator('.booking-review').innerText()).includes('10:00'));
  await page.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click();
  await page.waitForURL('**/meu-agendamento/confirmation');
  assert.equal(booking.customerEmail, 'customer@example.test'); assert.equal(booking.branchPublicId, 'b0'); assert.equal(booking.servicePublicId, 'hybrid'); assert.equal(booking.scheduledStart, '2026-09-22T13:00:00Z');
  const invalid = await page.goto(`http://localhost:${port}/agendamento/multi/foreign/hybrid`); assert.equal(invalid.status(), 404);
  console.log('PASS: single-branch skip, three-branch selection, scheduling-only hybrid actions, isolated back link, booking form and invalid branch');
} finally { await browser?.close(); child.kill(); fixture.close(); }
