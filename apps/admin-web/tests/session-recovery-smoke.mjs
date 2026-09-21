import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { fileURLToPath } from 'node:url';

const counts = new Map();
const api = createServer(async (request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.url === '/api/v1/auth/me') {
    response.statusCode = request.headers.authorization === 'Bearer fresh-access' ? 200 : 401;
    return response.end(JSON.stringify({ userId: 'test-user', role: 'Admin' }));
  }
  if (request.url !== '/api/v1/auth/refresh') return response.writeHead(404).end();
  let body = '';
  for await (const chunk of request) body += chunk;
  const { refreshToken } = JSON.parse(body);
  const count = (counts.get(refreshToken) ?? 0) + 1;
  counts.set(refreshToken, count);
  if (refreshToken === 'throttled' && count === 1) {
    response.writeHead(429, { 'Retry-After': '2' }); return response.end('{}');
  }
  if (refreshToken === 'unavailable' && count === 1) {
    response.writeHead(502, { 'Retry-After': '1' }); return response.end('{}');
  }
  if (refreshToken === 'lost' && count > 1) {
    response.writeHead(409); return response.end(JSON.stringify({ code: 'auth.refresh_conflict' }));
  }
  await new Promise(resolve => setTimeout(resolve, 50));
  response.end(JSON.stringify({ accessToken: 'fresh-access', refreshToken: `fresh-${refreshToken}` }));
});
await new Promise(resolve => api.listen(0, '127.0.0.1', resolve));
const reserve = createServer();
await new Promise(resolve => reserve.listen(0, '127.0.0.1', resolve));
const port = reserve.address().port;
await new Promise(resolve => reserve.close(resolve));
const origin = `http://127.0.0.1:${port}`;
const child = spawn(process.execPath, ['server.mjs', '--hostname', '127.0.0.1', '--port', String(port)], {
  cwd: fileURLToPath(new URL('../', import.meta.url)), windowsHide: true,
  env: { ...process.env, NODE_ENV: 'production', QUEUEFLOW_API_URL: `http://127.0.0.1:${api.address().port}`, QUEUEFLOW_PUBLIC_URL: origin, QUEUEFLOW_SECURE_COOKIES: 'false' },
  stdio: ['ignore', 'pipe', 'pipe'],
});
let output = '';
child.stdout.on('data', chunk => { output += chunk; });
child.stderr.on('data', chunk => { output += chunk; });
const destination = '/reports?page=2';
const path = `/api/auth/refresh?returnTo=${encodeURIComponent(destination)}`;
let proof;
const post = (token, extra = '', recoveryProof = proof) => fetch(origin + path, {
  method: 'POST', headers: { Origin: origin, Cookie: `queueflow_refresh=${token}; ${recoveryProof}; ${extra}` },
  redirect: 'manual', signal: AbortSignal.timeout(10000),
});
try {
  let ready = false;
  for (let attempt = 0; attempt < 90; attempt++) {
    if (child.exitCode !== null) throw new Error(output);
    try { await fetch(origin + '/login', { signal: AbortSignal.timeout(2000) }); ready = true; break; }
    catch { await new Promise(resolve => setTimeout(resolve, 500)); }
  }
  assert.ok(ready, output);
  const navigation = await fetch(origin + path, { redirect: 'manual' });
  assert.equal(navigation.status, 303);
  proof = navigation.headers.getSetCookie().find(value => value.startsWith('queueflow_refresh_attempt=')).split(';')[0];
  assert.ok(proof);
  const page = await fetch(navigation.headers.get('location'));
  assert.equal(page.status, 200);
  assert.match(page.headers.get('content-type'), /text\/html/);
  assert.match(await page.text(), /Recuperando sua/);
  assert.equal(counts.size, 0, 'GET and page rendering must never rotate');

  const limited = await post('throttled');
  assert.equal(limited.status, 429);
  assert.equal(limited.headers.get('retry-after'), '2');
  assert.equal(limited.headers.getSetCookie().length, 0);
  assert.equal((await post('throttled')).status, 429);
  assert.equal(counts.get('throttled'), 1);
  await new Promise(resolve => setTimeout(resolve, 2100));
  const resumed = await post('throttled');
  assert.equal(resumed.status, 200);
  assert.equal((await resumed.json()).redirectTo, destination);
  assert.match(resumed.headers.get('set-cookie'), /queueflow_access=fresh-access/);

  const unavailable = await post('unavailable');
  assert.equal(unavailable.status, 502);
  assert.equal(unavailable.headers.getSetCookie().length, 0);
  assert.equal((await post('unavailable')).status, 502);
  assert.equal(counts.get('unavailable'), 1);
  await new Promise(resolve => setTimeout(resolve, 1100));
  assert.equal((await post('unavailable')).status, 200);

  const concurrent = await Promise.all(Array.from({ length: 8 }, () => post('lost')));
  assert.ok(concurrent.every(response => response.status === 200));
  assert.equal(counts.get('lost'), 1);
  // Simulate dropping the winning response without persisting its Set-Cookie.
  await new Promise(resolve => setTimeout(resolve, 6000));
  assert.equal((await post('lost')).status, 200);
  assert.equal(counts.get('lost'), 1);
  const replay = await post('lost', '', 'queueflow_refresh_attempt=different-browser');
  assert.equal(replay.status, 503);
  assert.equal((await replay.json()).code, 'refresh_conflict');
  assert.equal(replay.headers.getSetCookie().length, 0);
  assert.equal((await post('lost', 'queueflow_access=fresh-access')).status, 200);
  assert.equal(counts.get('lost'), 2, 'fresh access must avoid another rotation');
  assert.equal((await fetch(origin + path, { method: 'POST', headers: { Origin: 'https://evil.test' } })).status, 403);
  console.log('Session recovery smoke passed: HTML, returnTo, cooldown, transient recovery, concurrency and proof-bound lost response.');
} finally {
  if (child.exitCode === null) { child.kill(); await once(child, 'exit'); }
  api.closeAllConnections();
  await new Promise(resolve => api.close(resolve));
}
