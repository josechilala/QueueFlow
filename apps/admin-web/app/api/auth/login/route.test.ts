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
  it.each([400, 429, 503])('handles completion HTTP %i without retries or bypassing backend validation', async status => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role: 'Owner' }))
      .mockResolvedValueOnce(Response.json({ completed: false, branchReady: true, servicesReady: true, operationReady: true }))
      .mockResolvedValueOnce(new Response(null, { status, headers: { 'Retry-After': '42' } }));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(fetchMock).toHaveBeenCalledTimes(4);
    if (status === 400) {
      expect(response.status).toBe(200);
      expect(await response.json()).toEqual({ authenticated: true, role: 'Owner', needsOnboarding: true });
    } else {
      expect(response.status).toBe(status);
      expect(response.headers.get('Set-Cookie')).toBeNull();
      expect(response.headers.get('Retry-After')).toBe('42');
    }
  });

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
      .mockResolvedValueOnce(Response.json(progress))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ authenticated: true, role: 'Owner', needsOnboarding: expected });
    const shouldComplete = !progress.completed && !expected;
    expect(fetchMock).toHaveBeenCalledTimes(shouldComplete ? 4 : 3);
    if (shouldComplete) {
      expect(fetchMock.mock.calls[3][0]).toMatch(/\/onboarding\/complete$/);
      expect(fetchMock.mock.calls[3][1]).toEqual(expect.objectContaining({ method: 'POST', headers: { Authorization: 'Bearer access' } }));
    }
    expect(fetchMock.mock.calls[2][0]).toMatch(/\/api\/v1\/onboarding$/);
    expect(fetchMock.mock.calls[2][1]).toEqual(expect.objectContaining({ headers: { Authorization: 'Bearer access' }, cache: 'no-store', redirect: 'error', signal: expect.any(AbortSignal) }));
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
    expect(response.status).toBe(503);
    expect(response.cookies.get('queueflow_access')).toBeUndefined();
  });
});

describe('login infrastructure resilience', () => {
  it.each(['/api/v1/auth/login', '/api/v1/auth/me', '/api/v1/onboarding', '/api/v1/onboarding/complete'])('identifies a 429 from %s without repeating authentication', async endpoint => {
    const responses = [
      Response.json({ accessToken: 'access', refreshToken: 'refresh' }),
      Response.json({ role: 'Owner' }),
      Response.json({ completed: false, branchReady: true, servicesReady: true, operationReady: true }),
    ];
    const stages = ['/api/v1/auth/login', '/api/v1/auth/me', '/api/v1/onboarding', '/api/v1/onboarding/complete'];
    const index = stages.indexOf(endpoint);
    const fetchMock = vi.fn();
    responses.slice(0, index).forEach(response => fetchMock.mockResolvedValueOnce(response));
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '30', 'X-RateLimit-Policy': 'global' } }));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(429);
    expect(response.headers.get('X-RateLimit-Endpoint')).toBe(endpoint);
    expect(response.headers.get('X-RateLimit-Policy')).toBe('global');
    expect(response.headers.get('Retry-After')).toBe('30');
    expect(response.headers.get('Set-Cookie')).toBeNull();
    expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/auth/login'))).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(index + 1);
  });

  it.each([401, 429, 502, 503, 504])('preserves HTTP %i and existing cookies without replaying login', async status => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('upstream private error', { status, headers: { 'Retry-After': '42', 'X-RateLimit-Policy': 'auth' } }));
    vi.stubGlobal('fetch', fetchMock);
    const req = request('10.0.0.2');
    req.headers.set('Cookie', 'queueflow_access=valid; queueflow_refresh=valid-refresh');
    const response = await POST(req);
    expect(response.status).toBe(status);
    expect(response.headers.get('Retry-After')).toBe('42');
    expect(response.headers.get('Set-Cookie')).toBeNull();
    expect(response.headers.get('Cache-Control')).toBe('no-store');
    if (status === 429) expect(response.headers.get('X-RateLimit-Policy')).toBe('auth');
    expect(JSON.stringify(await response.json())).not.toContain('upstream private error');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('recovers on a later explicit attempt after a network failure', async () => {
    const fetchMock = vi.fn().mockRejectedValueOnce(new TypeError('network'))
      .mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({ role: 'Admin' }));
    vi.stubGlobal('fetch', fetchMock);
    const failed = await POST(request('10.0.0.2'));
    expect(failed.status).toBe(502); expect(failed.headers.get('Set-Cookie')).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const recovered = await POST(request('10.0.0.2'));
    expect(recovered.status).toBe(200); expect(recovered.cookies.get('queueflow_access')?.value).toBe('access');
    expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/auth/login'))).toHaveLength(2);
  });

  it.each(['html', 'tokens', 'profile'])('handles malformed %s without changing existing cookies', async kind => {
    const fetchMock = vi.fn().mockResolvedValueOnce(kind === 'html' ? new Response('<html>Waking up</html>') : Response.json(kind === 'tokens' ? {} : { accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(Response.json({}));
    vi.stubGlobal('fetch', fetchMock);
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(502); expect(response.headers.get('Set-Cookie')).toBeNull();
    expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/auth/login'))).toHaveLength(1);
  });

  it('preserves rate limiting from the profile read without another credential POST', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json({ accessToken: 'access', refreshToken: 'refresh' }))
      .mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '12', 'X-RateLimit-Policy': 'global' } })));
    const response = await POST(request('10.0.0.2'));
    expect(response.status).toBe(429); expect(response.headers.get('Retry-After')).toBe('12');
    expect(response.headers.get('X-RateLimit-Policy')).toBe('global'); expect(response.headers.get('Set-Cookie')).toBeNull();
  });

  it('maps a bounded timeout to 504 without retrying or clearing cookies', async () => {
    const controller = new AbortController();
    const timeout = vi.spyOn(AbortSignal, 'timeout').mockReturnValue(controller.signal);
    try {
      const fetchMock = vi.fn().mockImplementation(async (_url, options) => {
        controller.abort(); options.signal.throwIfAborted();
      });
      vi.stubGlobal('fetch', fetchMock);
      const response = await POST(request('10.0.0.2'));
      expect(response.status).toBe(504); expect(response.headers.get('Set-Cookie')).toBeNull();
      expect(fetchMock).toHaveBeenCalledTimes(1); expect(timeout).toHaveBeenCalledWith(75_000);
    } finally { timeout.mockRestore(); }
  });
});
