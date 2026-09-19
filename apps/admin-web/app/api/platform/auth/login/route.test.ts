import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { IncomingMessage } from 'node:http';
import { POST } from './route';
import { createProxyTrust, stampClientIp } from '../../../../../lib/trusted-client-ip.mjs';

beforeEach(() => {
  vi.stubEnv('NODE_ENV', 'production');
  vi.stubEnv('QUEUEFLOW_API_URL', ' https://queueflow-api-2ujz.onrender.com/// ');
});
afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); });
const credentials = { email: 'platform@example.test', password: ' password-with-spaces ' };
function request(body: unknown = credentials, headers = new Headers()) {
  return new Request('https://admin.test/api/platform/auth/login', { method: 'POST', headers, body: JSON.stringify(body) });
}

it('uses the configured API origin and preserves credentials and platform cookies', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json({ accessToken: 'access', refreshToken: 'refresh' }));
  vi.stubGlobal('fetch', fetchMock);
  const response = await POST(request());
  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({ authenticated: true });
  expect(response.cookies.get('queueflow_platform_access')?.value).toBe('access');
  expect(response.cookies.get('queueflow_platform_refresh')?.value).toBe('refresh');
  expect(response.cookies.get('queueflow_access')).toBeUndefined();
  expect(fetchMock).toHaveBeenCalledExactlyOnceWith('https://queueflow-api-2ujz.onrender.com/api/v1/platform/auth/login', expect.objectContaining({
    body: JSON.stringify(credentials), cache: 'no-store', redirect: 'error', signal: expect.any(AbortSignal),
  }));
});

it.each(['', 'not-a-url', 'https://user:secret@api.test', 'https://api.test/api/v1', 'https://api.test?secret=x', 'https://api.test#fragment'])('rejects bad Production API configuration: %s', async url => {
  vi.stubEnv('QUEUEFLOW_API_URL', url);
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock);
  expect((await POST(request())).status).toBe(503);
  expect(fetchMock).not.toHaveBeenCalled();
});

it.each([401, 429, 500, 502, 503, 504, 403, 404])('preserves upstream HTTP %i without exposing its body', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('private upstream details', { status, headers: { 'Retry-After': '60' } })));
  const response = await POST(request());
  expect(response.status).toBe(status);
  expect(response.headers.get('Retry-After')).toBe('60');
  expect(response.headers.get('set-cookie')).toBeNull();
  const { message } = await response.json();
  expect(message).not.toContain('private upstream');
  if (status === 401) expect(message).toBe('E-mail ou senha inválidos.');
  else expect(message).not.toContain('E-mail ou senha inválidos');
});

it.each([{}, null, { email: 123, password: 'x' }, { email: 'x', password: '' }])('rejects invalid input', async body => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock);
  expect((await POST(request(body))).status).toBe(400);
  expect(fetchMock).not.toHaveBeenCalled();
});

it.each([new TypeError('fetch failed'), new DOMException('timeout', 'TimeoutError')])('returns 502 on transport failure', async error => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(error));
  expect((await POST(request())).status).toBe(502);
});

it.each(['not JSON', '{}', '{"accessToken":"access"}', '{"accessToken":"","refreshToken":"refresh"}'])('rejects invalid success responses', async body => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body)));
  const response = await POST(request());
  expect(response.status).toBe(502);
  expect(response.headers.get('set-cookie')).toBeNull();
});

it.each([false, true])('forwards only authenticated client IP headers (forged=%s)', async forged => {
  vi.stubEnv('QUEUEFLOW_INTERNAL_IP_KEY', 'synthetic-key');
  const incoming = { socket: { remoteAddress: '10.0.0.2' }, headers: { 'x-forwarded-for': '192.0.2.250, 192.0.2.1' } } as unknown as IncomingMessage;
  stampClientIp(incoming, createProxyTrust('10.0.0.2'), 'synthetic-key');
  const headers = new Headers(incoming.headers as Record<string, string>);
  if (forged) headers.set('x-queueflow-client-ip', '192.0.2.250');
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
  vi.stubGlobal('fetch', fetchMock);
  await POST(request(credentials, headers));
  expect(fetchMock.mock.calls[0][1].headers['X-Forwarded-For']).toBe(forged ? undefined : '192.0.2.1');
});
