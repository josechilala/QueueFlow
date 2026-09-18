import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { once } from 'node:events';

const received = [];
const api = createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.url === '/api/v1/auth/login') {
    received.push(request.headers['x-forwarded-for']);
    return response.end(JSON.stringify({ accessToken: 'test-access', refreshToken: 'test-refresh' }));
  }
  if (request.url === '/api/v1/auth/me') return response.end(JSON.stringify({ role: 'Admin' }));
  response.writeHead(404).end();
});
await new Promise(resolve => api.listen(0, '127.0.0.1', resolve));
const reserve = createServer();
await new Promise(resolve => reserve.listen(0, '127.0.0.1', resolve));
const port = reserve.address().port;
await new Promise(resolve => reserve.close(resolve));
const child = spawn(process.execPath, ['server.mjs', '--hostname', '127.0.0.1', '--port', String(port)], {
  cwd: new URL('..', import.meta.url),
  env: { ...process.env, NODE_ENV: 'production', QUEUEFLOW_TRUSTED_PROXIES: '127.0.0.1', QUEUEFLOW_API_URL: `http://127.0.0.1:${api.address().port}` },
  stdio: ['ignore', 'pipe', 'pipe'],
  windowsHide: true,
});
let output = '';
child.stdout.on('data', chunk => { output += chunk; });
child.stderr.on('data', chunk => { output += chunk; });
try {
  let ready = false;
  for (let attempt = 0; attempt < 90; attempt++) {
    if (child.exitCode !== null) throw new Error(output);
    try {
      await fetch(`http://127.0.0.1:${port}/login`, { signal: AbortSignal.timeout(2000) });
      ready = true;
      break;
    } catch { await new Promise(resolve => setTimeout(resolve, 500)); }
  }
  assert.ok(ready, output);
  for (const ip of ['192.0.2.1', '192.0.2.2']) {
    const response = await fetch(`http://127.0.0.1:${port}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Forwarded-For': `192.0.2.250, ${ip}`, 'X-QueueFlow-Client-IP': '192.0.2.250', 'X-QueueFlow-Client-IP-Signature': 'a'.repeat(64) },
      body: JSON.stringify({ email: 'admin@example.test', password: 'test-password' }),
      signal: AbortSignal.timeout(10000),
    });
    assert.equal(response.status, 200);
    assert.match(response.headers.get('set-cookie'), /queueflow_access=test-access/);
  }
  assert.deepEqual(received, ['192.0.2.1', '192.0.2.2']);
  console.log('Login BFF smoke passed: distinct client IPs, spoofed headers discarded, cookies preserved.');
} finally {
  if (child.exitCode === null) { child.kill(); await once(child, 'exit'); }
  api.closeAllConnections();
  await new Promise(resolve => api.close(resolve));
}
