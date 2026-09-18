import { afterEach, describe, expect, it, vi } from 'vitest';
import type { IncomingMessage } from 'node:http';
import { createProxyTrust, stampClientIp } from '../../../../lib/trusted-client-ip.mjs';
import { POST } from './route';

afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); });

function request(peer: string, forwarded?: string, proxies = '10.0.0.2') {
  vi.stubEnv('QUEUEFLOW_INTERNAL_IP_KEY', 'test-process-local-key');
  const incoming = { socket: { remoteAddress: peer }, headers: {
    'x-forwarded-for': forwarded,
    'x-queueflow-client-ip': '192.0.2.250',
    'x-queueflow-client-ip-signature': 'a'.repeat(64),
  } } as unknown as IncomingMessage;
  stampClientIp(incoming, createProxyTrust(proxies), process.env.QUEUEFLOW_INTERNAL_IP_KEY!);
  const headers = new Headers({ 'Content-Type': 'application/json' });
  for (const [name, value] of Object.entries(incoming.headers)) if (typeof value === 'string') headers.set(name, value);
  return new Request('http://admin.test/api/auth/login', { method: 'POST', headers, body: JSON.stringify({ email: 'owner@example.test', password: 'test-password' }) });
}

describe('login client IP forwarding', () => {
  it.each([
    ['10.0.0.2', '192.0.2.1', '192.0.2.1'],
    ['10.0.0.2', '192.0.2.250, 192.0.2.2', '192.0.2.2'],
    ['198.51.100.1', '192.0.2.250', '198.51.100.1'],
    ['10.0.0.2', 'bad, 192.0.2.1', '192.0.2.1'],
    ['10.0.0.2', '192.0.2.1, bad', '10.0.0.2'],
    ['::ffff:10.0.0.2', '2001:db8::1', '2001:db8::1'],
  ])('resolves peer %s / chain %s safely', async (peer, forwarded, expected) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    expect((await POST(request(peer, forwarded))).status).toBe(401);
    expect(fetchMock.mock.calls[0][1].headers['X-Forwarded-For']).toBe(expected);
  });

  it('stops at the first untrusted hop in a multi-proxy chain', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 429 }));
    vi.stubGlobal('fetch', fetchMock);
    expect((await POST(request('10.0.0.2', '192.0.2.250, 192.0.2.3, 10.0.0.3', '10.0.0.0/24'))).status).toBe(429);
    expect(fetchMock.mock.calls[0][1].headers['X-Forwarded-For']).toBe('192.0.2.3');
  });

  it('does not accept forged internal headers when bypassing the entry server', async () => {
    const incoming = request('10.0.0.2', '192.0.2.1');
    incoming.headers.set('x-queueflow-client-ip', '192.0.2.250');
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    await POST(incoming);
    expect(fetchMock.mock.calls[0][1].headers['X-Forwarded-For']).toBeUndefined();
  });

  it('preserves successful login and cookies', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role: 'Admin' }));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2', '192.0.2.1'));
    expect(response.status).toBe(200);
    expect(response.cookies.get('queueflow_access')?.value).toBe('access');
    expect(await response.json()).toEqual({ authenticated: true, role: 'Admin', needsOnboarding: false });
  });

  it('rejects unsafe or invalid proxy configuration', () => {
    for (const value of ['0.0.0.0/0', '::/0', 'garbage', '10.0.0.1/33']) expect(() => createProxyTrust(value)).toThrow();
    expect(createProxyTrust()('10.0.0.2')).toBe(false);
  });
});
