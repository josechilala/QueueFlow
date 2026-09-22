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

describe('post-login onboarding destination', () => {
  it.each([
    ['configured without explicit completion', { completed: false, branchReady: true, servicesReady: true, operationReady: true, nextStep: 'links' }, false],
    ['explicitly completed', { completed: true, branchReady: true, servicesReady: true, operationReady: true, nextStep: 'dashboard' }, false],
    ['previously completed with subsequent configuration changes', { completed: true, branchReady: false, servicesReady: false, operationReady: false, nextStep: 'dashboard' }, false],
    ['new organization', { completed: false, branchReady: false, servicesReady: false, operationReady: false, nextStep: 'branch' }, true],
    ['missing services', { completed: false, branchReady: true, servicesReady: false, operationReady: false, nextStep: 'services' }, true],
    ['missing queue or schedule', { completed: false, branchReady: true, servicesReady: true, operationReady: false, nextStep: 'operation' }, true],
  ])('uses backend progress for Owner: %s', async (_scenario, progress, expected) => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role: 'Owner' }))
      .mockResolvedValueOnce(Response.json(progress));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ authenticated: true, role: 'Owner', needsOnboarding: expected });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[2][0]).toMatch(/\/api\/v1\/onboarding$/);
    expect(fetchMock.mock.calls[2][1]).toEqual({ headers: { Authorization: 'Bearer access' }, cache: 'no-store' });
    expect(response.cookies.get('queueflow_access')?.value).toBe('access');
    expect(response.cookies.get('queueflow_refresh')?.value).toBe('refresh');
  });

  it.each(['Admin', 'Manager', 'Viewer', 'Attendant'])('preserves %s behavior without querying onboarding', async role => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role }));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(await response.json()).toEqual({ authenticated: true, role, needsOnboarding: false });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(response.cookies.get('queueflow_access')?.value).toBe(role === 'Attendant' ? undefined : 'access');
  });

  it('preserves the error response when onboarding cannot be read', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role: 'Owner' }))
      .mockResolvedValueOnce(new Response(null, { status: 503 })));
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(502);
    expect(response.cookies.get('queueflow_access')).toBeUndefined();
  });
});
