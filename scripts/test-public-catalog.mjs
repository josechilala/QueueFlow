import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const api = process.env.QUEUEFLOW_TEST_API_URL ?? 'http://localhost:5288';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(api).hostname));
let token;
async function call(path, method = 'GET', body, status = 200) {
  const response = await fetch(`${api}${path}`, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text(); assert.equal(response.status, status, `${path}: ${text}`); return text ? JSON.parse(text) : null;
}
const id = randomUUID().replaceAll('-', ''); const email = `${id}@test.local`; const password = `Qf!Catalog-${id}`;
await call('/api/v1/auth/register', 'POST', { name: 'Catalog', slug: `catalog-${id}`, timeZone: 'UTC', adminName: 'Owner', adminEmail: email, password }, 201);
token = (await call('/api/v1/auth/login', 'POST', { email, password })).accessToken;
const branch = await call('/api/v1/branches', 'POST', { name: 'Salon', timeZone: 'UTC' }, 201);
const other = await call('/api/v1/branches', 'POST', { name: 'Other', timeZone: 'UTC' }, 201);
const services = [];
for (const name of ['A', 'B', 'C']) services.push(await call('/api/v1/services', 'POST', { branchId: branch.id, name, prefix: name, averageDurationMinutes: 10 }, 201));
const settings = { attendanceMode: 'AppointmentOnly', slotDurationMinutes: 30, capacityPerSlot: 1, minimumAdvanceMinutes: 0, maximumAdvanceDays: 10, lateToleranceMinutes: 10, cancellationDeadlineMinutes: 0, checkInAdvanceMinutes: 60, allowCustomerCancellation: true, allowCustomerReschedule: true, requireConfirmation: false, isActive: true };
await call(`/api/v1/services/${services[2].id}/scheduling-settings`, 'PUT', settings);
const queuePayload = service => ({ branchId: branch.id, serviceId: service.id, name: 'Identical queue name' });
for (const service of services.slice(0, 2)) { service.queue = await call('/api/v1/queues', 'POST', queuePayload(service), 201); await call(`/api/v1/queues/${service.queue.id}/open`, 'POST'); }
const catalog = await call(`/api/v1/public/branches/${branch.publicId}`);
assert.equal(catalog.services.length, 3);
for (const item of catalog.services) { assert.equal(item.canJoinQueue, item.name !== 'C'); assert.equal(item.canSchedule, item.name === 'C'); }
await call('/api/v1/queues', 'POST', queuePayload(services[0]), 400);
await call('/api/v1/queues', 'POST', { ...queuePayload(services[0]), branchId: other.id }, 400);
// Independent HTTP requests exercise the database constraint as well as the application check.
const extra = await call('/api/v1/services', 'POST', { branchId: branch.id, name: 'Concurrent', prefix: 'CC', averageDurationMinutes: 10 }, 201);
const results = await Promise.all(Array.from({ length: 5 }, () => fetch(`${api}/api/v1/queues`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: JSON.stringify(queuePayload(extra)) })));
assert.equal(results.filter(r => r.status === 201).length, 1); assert.equal(results.filter(r => r.status === 400).length, 4);
const ownerToken = token; token = undefined;
const foreignId = randomUUID().replaceAll('-', ''); const foreignEmail = `${foreignId}@test.local`;
await call('/api/v1/auth/register', 'POST', { name: 'Foreign', slug: `foreign-${foreignId}`, timeZone: 'UTC', adminName: 'Owner', adminEmail: foreignEmail, password }, 201);
token = (await call('/api/v1/auth/login', 'POST', { email: foreignEmail, password })).accessToken;
await call('/api/v1/queues', 'POST', queuePayload(services[0]), 400);
token = ownerToken;
if (process.env.QUEUEFLOW_TEST_CUSTOMER_URL) {
  const response = await fetch(`${process.env.QUEUEFLOW_TEST_CUSTOMER_URL}/unidade/${branch.publicId}`);
  assert.equal(response.status, 200); const html = await response.text();
  assert.ok(html.includes(`/q/${services[0].queue.publicId}`)); assert.ok(html.includes(`/q/${services[1].queue.publicId}`));
  assert.ok(html.includes(`/agendar/${branch.publicId}/${services[2].publicId}`)); assert.ok(html.includes('Como podemos ajudar?'));
}
console.log('PASS: three-service catalog, mode actions, exact queue links, duplicate rejection, concurrent creation, cross-branch and cross-tenant rejection');
