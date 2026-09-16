import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { fileURLToPath } from 'node:url';

const api = createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  const url = request.url;
  if (url === '/api/v1/auth/login') return response.end(JSON.stringify({ accessToken: 'tenant-test-access', refreshToken: 'tenant-test-refresh' }));
  if (request.headers.authorization === 'Bearer tenant-test-access') {
    if (url === '/api/v1/auth/me') return response.end(JSON.stringify({ userId: 'owner', organizationId: 'tenant', name: 'Owner', email: 'owner@example.test', role: 'Owner' }));
    if (url === '/api/v1/onboarding') return response.end(JSON.stringify({ completed: false, branchReady: true, servicesReady: false, operationReady: false, nextStep: 'services' }));
    if (url === '/api/v1/onboarding/complete') { response.statusCode = 204; return response.end(); }
  }
  if (url === '/api/v1/platform/auth/login') return response.end(JSON.stringify({ accessToken: 'platform-test-access', refreshToken: 'platform-test-refresh' }));
  if (request.headers.authorization !== 'Bearer platform-test-access') { response.statusCode = 401; return response.end('{}'); }
  if (url === '/api/v1/platform/auth/me') return response.end(JSON.stringify({ userId: 'test-admin', name: 'Test admin', email: 'admin@example.test' }));
  if (url === '/api/v1/platform/dashboard') return response.end(JSON.stringify({ totalOrganizations: 0, activeOrganizations: 0, trials: 0, activeSubscriptions: 0, pendingInvitations: 0, expiredInvitations: 0 }));
  if (url === '/api/v1/platform/invitations' && request.method === 'POST') { response.statusCode = 201; return response.end(JSON.stringify({ invitation: { id: 'test-invitation', email: 'owner@example.test', status: 'Pending' }, activationUrl: 'https://customer.example.test/ativar#test-token' })); }
  if (url === '/api/v1/platform/invitations/test-invitation/revoke') { response.statusCode = 204; return response.end(); }
  response.statusCode = 404; response.end('{}');
});
api.listen(0, '127.0.0.1');
await once(api, 'listening');
const reservation = createServer();
reservation.listen(0, '127.0.0.1');
await once(reservation, 'listening');
const port = reservation.address().port;
await new Promise(resolve => reservation.close(resolve));
const app = spawn(process.execPath, ['../../node_modules/next/dist/bin/next', 'start', '-p', String(port)], {
  cwd: fileURLToPath(new URL('../', import.meta.url)),
  windowsHide: true,
  env: { ...process.env, QUEUEFLOW_API_URL: 'http://127.0.0.1:' + api.address().port, QUEUEFLOW_PUBLIC_URL: 'http://localhost:' + port, QUEUEFLOW_SECURE_COOKIES: 'false' },
  stdio: ['ignore', 'pipe', 'pipe'],
});
let output = '';
app.stdout.on('data', chunk => { output += chunk; });
app.stderr.on('data', chunk => { output += chunk; });
const origin = 'http://localhost:' + port;
try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { const response = await fetch(origin + '/platform/login', { redirect: 'manual' }); if (response.status === 200) { ready = true; break; } } catch {}
    if (app.exitCode !== null) throw new Error(output);
    await new Promise(resolve => setTimeout(resolve, 300));
  }
  assert.ok(ready, 'Platform login must render without a redirect loop: ' + output);
  const login = await fetch(origin + '/api/platform/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'admin@example.test', password: 'test-password' }) });
  assert.equal(login.status, 200);
  const cookies = login.headers.getSetCookie();
  assert.equal(cookies.length, 2);
  assert.ok(cookies.every(cookie => /Path=\/(;|$)/i.test(cookie) && /HttpOnly/i.test(cookie)));
  assert.ok(cookies.every(cookie => cookie.startsWith('queueflow_platform_')));
  const cookie = cookies.map(value => value.split(';')[0]).join('; ');
  const dashboard = await fetch(origin + '/platform', { headers: { Cookie: cookie }, redirect: 'manual' });
  assert.equal(dashboard.status, 200);
  const created = await fetch(origin + '/api/platform/invitations', { method: 'POST', headers: { Cookie: cookie, 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'owner@example.test' }) });
  assert.equal(created.status, 201);
  assert.equal((await created.json()).invitation.id, 'test-invitation');
  const revoked = await fetch(origin + '/api/platform/invitations/test-invitation/revoke', { method: 'POST', headers: { Cookie: cookie } });
  assert.equal(revoked.status, 204);
  assert.equal(await revoked.text(), '');
  const tenantCookies = await fetch(origin + '/api/platform/invitations', { method: 'POST', headers: { Cookie: 'queueflow_access=tenant-access', 'Content-Type': 'application/json' }, body: '{}' });
  assert.equal(tenantCookies.status, 401);
  const ownerLogin = await fetch(origin + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'owner@example.test', password: 'test-password' }) });
  assert.equal(ownerLogin.status, 200);
  assert.equal((await ownerLogin.json()).needsOnboarding, true, 'Existing branch does not bypass incomplete onboarding');
  const ownerCookie = ownerLogin.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  const completed = await fetch(origin + '/api/onboarding/complete', { method: 'POST', headers: { Cookie: ownerCookie, 'Content-Type': 'application/json' }, body: '{}' });
  assert.equal(completed.status, 204, 'BFF must preserve empty 204 response');
  const logout = await fetch(origin + '/api/platform/auth/logout', { method: 'POST', headers: { 'x-forwarded-host': 'untrusted.example' }, redirect: 'manual' });
  assert.equal(logout.headers.get('location'), origin + '/platform/login');
  console.log('Platform HTTP smoke passed: public login, separate cookies, dashboard, create, revoke, tenant rejection.');
} finally {
  app.kill();
  await new Promise(resolve => api.close(resolve));
}
