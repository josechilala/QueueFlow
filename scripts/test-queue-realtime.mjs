import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

// Run against a local test API: QUEUEFLOW_TEST_API_URL=http://localhost:5288 node scripts/test-queue-realtime.mjs
// Creates an isolated organization; its ID is printed for inspection/cleanup.
const api = process.env.QUEUEFLOW_TEST_API_URL ?? 'http://localhost:5288';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(api).hostname), 'Use a local test API');
const stamp = randomUUID().replaceAll('-', '');
const email = `realtime-${stamp}@test.local`;
const password = `Qf!Realtime-${stamp}`;
let accessToken;
async function request(path, method = 'GET', body, authenticated = true) {
  const response = await fetch(`${api}${path}`, {
    method, headers: { 'Content-Type': 'application/json', ...(authenticated && accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  assert.ok(response.ok, `${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : undefined;
}
async function until(predicate, message) {
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) { if (await predicate()) return; await new Promise(resolve => setTimeout(resolve, 50)); }
  assert.fail(message);
}
const connections = [];
async function connect(method, id) {
  const events = [];
  const connection = new HubConnectionBuilder().withUrl(`${api}/hubs/queue`).withAutomaticReconnect([0, 100, 500]).configureLogging(LogLevel.Error).build();
  connection.on('QueueUpdated', payload => { events.push({ event: 'QueueUpdated', ...payload }); });
  connection.on('TicketCalled', payload => { events.push({ event: 'TicketCalled', ...payload }); });
  connection.onreconnected(() => connection.invoke(method, id));
  await connection.start(); await connection.invoke(method, id);
  connections.push(connection);
  return { connection, events };
}
let browser;
try {
  const organization = await request('/api/v1/auth/register', 'POST', { name: 'Realtime Test', slug: `rt-${stamp}`, timeZone: 'America/Sao_Paulo', adminName: 'Realtime Owner', adminEmail: email, password }, false);
  console.log(`Test organization: ${organization.organizationId}`);
  accessToken = (await request('/api/v1/auth/login', 'POST', { email, password }, false)).accessToken;
  const branch = await request('/api/v1/branches', 'POST', { name: 'Realtime Branch', timeZone: 'UTC' });
  const service = await request('/api/v1/services', 'POST', { branchId: branch.id, name: 'Realtime Service', prefix: 'RT', averageDurationMinutes: 5 });
  const counter = await request('/api/v1/counters', 'POST', { branchId: branch.id, name: 'Realtime Counter' });
  const queue = await request('/api/v1/queues', 'POST', { branchId: branch.id, serviceId: service.id, name: 'Realtime Queue', capacity: 50 });
  const other = await request('/api/v1/queues', 'POST', { branchId: branch.id, serviceId: service.id, name: 'Other Queue', capacity: 50 });
  await request(`/api/v1/queues/${queue.id}/open`, 'POST');
  const attendant = await connect('JoinQueueGroup', queue.publicId);
  const display = await connect('JoinQueueGroup', queue.publicId);
  const unrelated = await connect('JoinQueueGroup', other.publicId);
  const issue = () => request(`/api/v1/public/queues/${queue.publicId}/tickets`, 'POST', { priority: 'Normal' }, false);
  const first = await issue();
  const second = await issue();
  const customer = await connect('JoinTicketGroup', second.customerPublicToken);
  const third = await issue();
  await until(() => [attendant, display, customer].every(client => client.events.some(event => event.event === 'QueueUpdated' && event.waitingCount === 3)), 'All subscribers should see new arrivals');
  for (const event of attendant.events) {
    assert.equal(event.queueId, queue.id);
    if (event.event === 'QueueUpdated') assert.ok(Number.isFinite(Date.parse(event.timestamp)));
    assert.ok(!JSON.stringify(event).includes('customerPublicToken'));
  }
  console.log('PASS: arrival, waitingCount, queue group and ticket-to-queue subscription');

  let pages;
  if (process.env.QUEUEFLOW_PLAYWRIGHT_MODULE) {
    const { chromium } = await import(process.env.QUEUEFLOW_PLAYWRIGHT_MODULE);
    browser = await chromium.launch({ headless: true, channel: 'chromium' });
    const context = await browser.newContext();
    context.on('page', page => page.on('pageerror', error => console.error('Browser error:', error.message)));
    context.setDefaultTimeout(90000);
    context.setDefaultNavigationTimeout(90000);
    const customerPage = await context.newPage();
    const displayPage = await context.newPage();
    const attendantPage = await context.newPage();
    await attendantPage.goto('http://localhost:3203/login');
    await attendantPage.waitForFunction(() => {
      const form = document.querySelector('form');
      return form && Object.keys(form).some(key => key.startsWith('__reactProps') && typeof form[key]?.onSubmit === 'function');
    });
    await attendantPage.getByLabel('E-mail').fill(email);
    await attendantPage.getByLabel('Senha', { exact: true }).fill(password);
    await attendantPage.getByRole('button', { name: 'Entrar', exact: true }).click();
    await attendantPage.waitForURL('**/workstation');
    await attendantPage.locator('.selectors select').nth(1).selectOption(queue.id);
    await attendantPage.locator('.selectors select').nth(2).selectOption(counter.id);
    await customerPage.goto(`http://localhost:3201/ticket/${second.customerPublicToken}`);
    await displayPage.goto(`http://localhost:3202/q/${queue.publicId}`);
    await until(async () => (await attendantPage.locator('.metrics article').first().innerText()).includes('3'), 'Attendant initial waiting count');
    await until(async () => (await displayPage.locator('.connection').innerText()).includes('conectado'), 'Display connection');
    pages = { customerPage, displayPage, attendantPage };
    const extra = await issue();
    await until(async () => (await attendantPage.locator('.metrics article').first().innerText()).includes('4'), 'Attendant should refetch after another arrival');
    await request(`/api/v1/tickets/${extra.id}/cancel`, 'POST', { reason: 'Realtime test' });
    await until(async () => (await attendantPage.locator('.metrics article').first().innerText()).includes('3'), 'Attendant should refetch after cancellation');
  }

  const called = await request(`/api/v1/queues/${queue.id}/call-next`, 'POST', { counterId: counter.id });
  assert.equal(called.id, first.id);
  await until(() => display.events.some(event => event.event === 'TicketCalled' && event.ticketId === first.id), 'Display should receive TicketCalled');
  const call = display.events.find(event => event.event === 'TicketCalled' && event.ticketId === first.id);
  assert.equal(call.counterId, counter.id); assert.equal(call.counterName, 'Realtime Counter'); assert.equal(call.ticketNumber, first.ticketNumber);
  await until(() => customer.events.some(event => event.event === 'QueueUpdated' && event.waitingCount === 2), 'Waiting customer should see another ticket called');
  const status = await request(`/api/v1/public/tickets/${second.customerPublicToken}`, 'GET', undefined, false);
  assert.equal(status.position, 1); assert.equal(status.ticketsAhead, 0);
  if (pages) {
    await until(async () => (await pages.displayPage.locator('.current-call').innerText()).includes(first.ticketNumber), 'Display should change without reload');
    await until(async () => Number.parseInt(await pages.customerPage.locator('.public-metrics strong').first().innerText(), 10) === 1, 'Customer position should change without reload');
  }
  await request(`/api/v1/tickets/${first.id}/start`, 'POST');
  const before = customer.events.length;
  await request(`/api/v1/tickets/${first.id}/complete`, 'POST');
  await until(() => customer.events.length > before, 'Completion should notify the queue');
  console.log('PASS: call, counter payload, position, start, completion');

  // A second call checks the customer status and recovery of the display snapshot.
  await request(`/api/v1/queues/${queue.id}/call-next`, 'POST', { counterId: counter.id });
  await until(() => customer.events.some(event => event.event === 'TicketCalled' && event.ticketId === second.id), 'Called customer notification');
  if (pages) {
    await until(async () => (await pages.customerPage.locator('.ticket-called').count()) === 1, 'Customer Called status without reload');
    await until(async () => (await pages.attendantPage.locator('.operation-card').innerText()).includes(second.ticketNumber), 'Attendant current ticket without reload');
    const browserContext = pages.customerPage.context();
    await browserContext.setOffline(true);
    await until(async () => !(await pages.displayPage.locator('.connection').innerText()).includes('conectado'), 'Display should detect the lost connection');
    await browserContext.setOffline(false);
    await until(async () => (await pages.displayPage.locator('.connection').innerText()).includes('conectado'), 'Display should reconnect');
  }
  await display.connection.stop();
  await request(`/api/v1/tickets/${second.id}/recall`, 'POST');
  await display.connection.start(); await display.connection.invoke('JoinQueueGroup', queue.publicId);
  const snapshot = await request(`/api/v1/public/queues/${queue.publicId}`, 'GET', undefined, false);
  assert.equal(snapshot.latestCalls[0].ticketId, second.id);
  await request(`/api/v1/tickets/${second.id}/start`, 'POST');
  await request(`/api/v1/tickets/${second.id}/complete`, 'POST');
  if (pages) {
    await until(async () => (await pages.customerPage.locator('.ticket-completed').count()) === 1, 'Customer completion without reload');
    await until(async () => (await pages.attendantPage.locator('.operation-card').innerText()).includes('Nenhum ticket'), 'Attendant completion without reload');
    console.log('PASS: three browser pages, live counts, display call/history, customer position/status, reconnect, completion without F5');
  }
  await request(`/api/v1/queues/${queue.id}/call-next`, 'POST', { counterId: counter.id });
  await request(`/api/v1/tickets/${third.id}/no-show`, 'POST');
  const fourth = await issue();
  await request(`/api/v1/tickets/${fourth.id}/cancel`, 'POST', { reason: 'Realtime test' });
  await until(() => attendant.events.some(event => event.event === 'QueueUpdated' && event.waitingCount === 0), 'Final waiting count');
  assert.equal((await request(`/api/v1/public/tickets/${third.customerPublicToken}`, 'GET', undefined, false)).status, 'NoShow');
  assert.equal((await request(`/api/v1/public/tickets/${fourth.customerPublicToken}`, 'GET', undefined, false)).status, 'Cancelled');
  assert.equal(unrelated.events.length, 0, 'Other queues must not receive the scoped events');
  console.log('PASS: recall, resubscription, authoritative display history, no-show, cancellation, queue isolation');
  const beforePause = customer.events.length;
  await request(`/api/v1/queues/${queue.id}/pause`, 'POST');
  await until(() => customer.events.length > beforePause, 'Queue pause notification');
  await request(`/api/v1/queues/${queue.id}/open`, 'POST');
  // Check-in creates a ticket through a separate persistence path and its durable outbox.
  const target = new Date(Math.ceil((Date.now() + 60000) / 1800000) * 1800000);
  await request(`/api/v1/services/${service.id}/scheduling-settings`, 'PUT', {
    attendanceMode: 'Hybrid', slotDurationMinutes: 30, capacityPerSlot: 1, minimumAdvanceMinutes: 0,
    maximumAdvanceDays: 10, lateToleranceMinutes: 10, cancellationDeadlineMinutes: 0,
    checkInAdvanceMinutes: 60, allowCustomerCancellation: true, allowCustomerReschedule: true,
    requireConfirmation: false, isActive: true,
  });
  await request(`/api/v1/services/${service.id}/schedules`, 'POST', {
    dayOfWeek: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][target.getUTCDay()],
    startTime: '00:00:00', endTime: '23:59:59',
  });
  const appointment = await request('/api/v1/public/appointments', 'POST', {
    branchPublicId: branch.publicId, servicePublicId: service.publicId, scheduledStart: target.toISOString(),
    customerName: 'Realtime Appointment', customerEmail: 'appointment@test.local',
  }, false);
  const beforeCheckIn = customer.events.length;
  await request(`/api/v1/public/appointments/${appointment.publicToken}/check-in`, 'POST', {}, false);
  await until(() => customer.events.slice(beforeCheckIn).some(event => event.event === 'QueueUpdated' && event.waitingCount === 1), 'Check-in outbox should reach API connections without Redis');
  assert.equal(unrelated.events.length, 0);
  console.log('PASS: queue transitions and check-in outbox delivery without Redis');

} finally {
  await browser?.close();
  await Promise.all(connections.map(connection => connection.stop()));
}
