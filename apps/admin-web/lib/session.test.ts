import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cookies } from 'next/headers';
import { tenantSession, platformSession } from './session';
import { attendantSession } from '../../attendant-web/lib/session';
import { GET as sessionRoute } from '../app/api/auth/session/route';
import { forwardAuthenticatedDelete, forwardAuthenticatedJson } from './server-api-proxy';
import { forwardPlatformJson } from './server-platform-proxy';
import { post as attendantPost } from '../../attendant-web/lib/api-proxy';

vi.mock('next/headers', () => ({ cookies: vi.fn() }));
let sequence = 0;
let jar: Map<string, string>;
const pair = { accessToken: 'new-access', refreshToken: 'new-refresh' };
const request = (path = '/api/auth/refresh') => new Request(`https://admin.example.test${path}`);
const mockFetch = (...responses: Response[]) => {
  const fetch = vi.fn();
  for (const response of responses) fetch.mockResolvedValueOnce(response);
  vi.stubGlobal('fetch', fetch);
  return fetch;
};
beforeEach(() => {
  jar = new Map();
  vi.mocked(cookies).mockImplementation(async () => ({ get: (name: string) => jar.has(name) ? { value: jar.get(name) } : undefined }) as never);
  vi.stubEnv('QUEUEFLOW_API_URL', 'https://api.example.test');
  vi.stubEnv('QUEUEFLOW_PUBLIC_URL', 'https://admin.example.test');
});
afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.useRealTimers(); });

describe.each([
  ['tenant', tenantSession, 'queueflow_access', 'queueflow_refresh', 'auth.invalid_refresh', '/dashboard'],
  ['platform', platformSession, 'queueflow_platform_access', 'queueflow_platform_refresh', 'platform.invalid_refresh', '/platform'],
  ['attendant', attendantSession, 'queueflow_attendant_access', 'queueflow_attendant_refresh', 'auth.invalid_refresh', '/workstation'],
] as const)('%s session', (_, session, accessCookie, refreshCookie, code, home) => {
  beforeEach(() => { jar.set(refreshCookie, `old-refresh-${++sequence}`); if (session === tenantSession) jar.set('queueflow_refresh_attempt', `proof-${sequence}`); });

  it('renews an expired access cookie and persists both secure cookie attributes', async () => {
    const fetch = mockFetch(Response.json(pair));
    const response = await session.refresh(request());
    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe(`https://admin.example.test${home}`);
    expect(response.headers.get('cache-control')).toBe('no-store');
    expect(response.cookies.get(accessCookie)).toMatchObject({ value: pair.accessToken, httpOnly: true, sameSite: 'lax', path: '/', maxAge: 900 });
    expect(response.cookies.get(refreshCookie)).toMatchObject({ value: pair.refreshToken, maxAge: 2592000 });
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ refreshToken: jar.get(refreshCookie) });
  });

  it.each([429, 500, 502, 503, 504, 409, 403, 400])('preserves cookies and never redirects on refresh HTTP %i', async status => {
    mockFetch(Response.json({}, { status, headers: { 'Retry-After': '42' } }));
    const response = await session.refresh(request());
    expect(response.status).toBe(status === 429 || status >= 500 ? status : 503);
    expect(response.headers.get('retry-after')).toBe('42');
    expect(response.headers.has('set-cookie')).toBe(false);
    expect(response.headers.has('location')).toBe(false);
  });

  it.each([new TypeError('fetch failed'), new DOMException('timeout', 'TimeoutError')])('preserves cookies on transport failure: %s', async error => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(error));
    const response = await session.refresh(request());
    expect(response.status).toBe(503);
    expect(response.headers.has('set-cookie')).toBe(false);
    expect(response.headers.has('location')).toBe(false);
  });

  it.each([200, 401])('does not mistake malformed HTTP %i for definite rejection', async status => {
    mockFetch(new Response('<html>gateway error</html>', { status }));
    const response = await session.refresh(request());
    expect(response.status).toBe(503);
    expect(response.headers.has('set-cookie')).toBe(false);
  });

  it('clears only its own cookies on a definitive API rejection', async () => {
    mockFetch(Response.json({ code }, { status: 401 }));
    const response = await session.refresh(request());
    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toContain('reason=session_expired');
    expect(response.cookies.getAll().map(c => c.name).sort()).toEqual([accessCookie, refreshCookie].sort());
    expect(response.cookies.getAll().every(c => c.maxAge === 0)).toBe(true);
  });

  it('does not delete cookies when this request has no refresh cookie', async () => {
    jar.clear();
    const fetch = mockFetch();
    const response = await session.refresh(request());
    expect(response.status).toBe(303);
    expect(response.headers.has('set-cookie')).toBe(false);
    expect(fetch).not.toHaveBeenCalled();
  });

  it('coalesces parallel refreshes and late arrivals with the old cookie', async () => {
    const fetch = mockFetch(Response.json(pair));
    const responses = await Promise.all(Array.from({ length: 8 }, () => session.refresh(request())));
    responses.push(await session.refresh(request()));
    expect(fetch).toHaveBeenCalledTimes(1);
    for (const response of responses) {
      expect(response.status).toBe(303);
      expect(response.cookies.get(refreshCookie)?.value).toBe(pair.refreshToken);
    }
  });

  it('holds temporary errors during cooldown then allows recovery', async () => {
    vi.useFakeTimers();
    const fetch = mockFetch(new Response(null, { status: 502 }), Response.json(pair));
    expect((await session.refresh(request())).status).toBe(502);
    expect((await session.refresh(request())).status).toBe(502);
    expect(fetch).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(5_001);
    expect((await session.refresh(request())).status).toBe(303);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('respects Retry-After across repeated and concurrent attempts', async () => {
    vi.useFakeTimers();
    const fetch = mockFetch(new Response(null, { status: 429, headers: { 'Retry-After': '60' } }), Response.json(pair));
    await session.refresh(request());
    await vi.advanceTimersByTimeAsync(10_000);
    const responses = await Promise.all(Array.from({ length: 8 }, () => session.refresh(request())));
    expect(fetch).toHaveBeenCalledTimes(1);
    for (const response of responses) {
      expect(response.status).toBe(429);
      expect(response.headers.get('retry-after')).toBe('50');
      expect(response.headers.has('set-cookie')).toBe(false);
    }
    await vi.advanceTimersByTimeAsync(50_001);
    expect((await session.refresh(request())).status).toBe(303);
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('exposes a conflict without returning credentials or clearing the winner cookies', async () => {
    mockFetch(Response.json({ code: code.replace('invalid_refresh', 'refresh_conflict') }, { status: 409 }));
    const response = await session.refresh(request());
    expect(response.status).toBe(503);
    expect(await response.json()).toMatchObject({ code: 'refresh_conflict' });
    expect(response.headers.has('set-cookie')).toBe(false);
  });

  it('bounds the successful rotation cache and never accepts a subsequent API replay conflict', async () => {
    vi.useFakeTimers();
    const fetch = mockFetch(Response.json(pair), new Response(null, { status: 409 }));
    expect((await session.refresh(request())).status).toBe(303);
    await vi.advanceTimersByTimeAsync(session === tenantSession ? 60_001 : 5_001);
    const late = await session.refresh(request());
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(late.status).toBe(503);
    expect(late.headers.has('set-cookie')).toBe(false);
  });

  it('preserves cookies if the upstream response body fails during transport', async () => {
    const stream = new ReadableStream({ start(controller) { controller.error(new TypeError('connection reset')); } });
    mockFetch(new Response(stream));
    const response = await session.refresh(request());
    expect(response.status).toBe(503);
    expect(response.headers.has('set-cookie')).toBe(false);
  });

  it('renews once after API 401, then retries with the new access token', async () => {
    jar.set(accessCookie, 'expired-access');
    const fetch = mockFetch(new Response(null, { status: 401 }), Response.json(pair), Response.json({ ok: true }));
    const response = await session.forward('/protected', { method: 'POST', body: '{"value":1}' });
    expect(response.status).toBe(200);
    expect(response.cookies.get(refreshCookie)?.value).toBe(pair.refreshToken);
    expect(fetch).toHaveBeenCalledTimes(3);
    expect(fetch.mock.calls[2][1].headers.get('Authorization')).toBe(`Bearer ${pair.accessToken}`);
    expect(fetch.mock.calls[2][1].body).toBe('{"value":1}');
  });

  it.each([401, 429, 502])('persists a rotated pair even if the subsequent request returns %i', async status => {
    mockFetch(Response.json(pair), new Response(null, { status }));
    const response = await session.forward('/protected');
    expect(response.status).toBe(status === 401 ? 503 : status);
    expect(response.cookies.get(refreshCookie)?.value).toBe(pair.refreshToken);
    expect(response.cookies.getAll().every(c => c.maxAge! > 0)).toBe(true);
  });

  it.each([403, 429, 500, 502, 503])('does not refresh or erase cookies on resource HTTP %i', async status => {
    jar.set(accessCookie, 'valid-access');
    const fetch = mockFetch(new Response(null, { status }));
    const response = await session.forward('/protected');
    expect(response.status).toBe(status);
    expect(response.headers.has('set-cookie')).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('clears cookies only after explicit refresh rejection during a resource request', async () => {
    mockFetch(Response.json({ code }, { status: 401 }));
    const response = await session.forward('/protected');
    expect(response.status).toBe(401);
    expect(response.cookies.getAll().every(c => c.maxAge === 0)).toBe(true);
  });

  it('does not log out a concurrent request when another API instance rotated the token', async () => {
    mockFetch(Response.json({ code: 'auth.refresh_conflict' }, { status: 409 }));
    const response = await session.forward('/protected');
    expect(response.status).toBe(503);
    expect(response.headers.has('set-cookie')).toBe(false);
  });

  it.each(['//evil.test', '/\\evil.test', '/api/auth/refresh', 'https://evil.test'])('rejects unsafe returnTo %s', async path => {
    mockFetch(Response.json(pair));
    const response = await session.refresh(request(`/api/auth/refresh?returnTo=${encodeURIComponent(path)}`));
    expect(response.headers.get('location')).toBe(`https://admin.example.test${home}`);
  });

  it('preserves a local destination and its query after renewal', async () => {
    mockFetch(Response.json(pair));
    const response = await session.refresh(request('/api/auth/refresh?returnTo=%2Fprotected%3Fpage%3D2'));
    expect(response.headers.get('location')).toBe('https://admin.example.test/protected?page=2');
  });
});

it('tenant and platform cookies do not share a renewal', async () => {
  jar.set('queueflow_refresh', `both-${++sequence}`);
  jar.set('queueflow_platform_refresh', jar.get('queueflow_refresh')!);
  const fetch = mockFetch(Response.json(pair), Response.json(pair));
  await Promise.all([tenantSession.refresh(request()), platformSession.refresh(request())]);
  expect(fetch).toHaveBeenCalledTimes(2);
});

it.each([
  ['session', () => sessionRoute(), 'queueflow_access'],
  ['delete', () => forwardAuthenticatedDelete('/protected'), 'queueflow_access'],
  ['post', () => forwardAuthenticatedJson(new Request('https://admin.test', { method: 'POST', body: '{}' }), '/protected', 'POST'), 'queueflow_access'],
  ['platform post', () => forwardPlatformJson(new Request('https://admin.test', { method: 'POST', body: '{}' }), '/protected', 'POST'), 'queueflow_platform_access'],
  ['attendant post', () => attendantPost('/protected', new Request('https://attendant.test', { method: 'POST', body: '{}' })), 'queueflow_attendant_access'],
] as const)('%s route preserves 429 and Retry-After', async (_, execute, cookie) => {
  jar.set(cookie, 'access');
  mockFetch(new Response(null, { status: 429, headers: { 'Retry-After': '20' } }));
  const response = await execute();
  expect(response.status).toBe(429);
  expect(response.headers.get('retry-after')).toBe('20');
  expect(response.headers.has('set-cookie')).toBe(false);
});

it('recovers a lost response with browser proof but not with the old token alone', async () => {
  vi.useFakeTimers();
  jar.set('queueflow_refresh', `lost-${++sequence}`);
  jar.set('queueflow_refresh_attempt', 'separate-browser-proof');
  const fetch = mockFetch(Response.json(pair), Response.json({ code: 'auth.refresh_conflict' }, { status: 409 }));
  await tenantSession.refresh(request());
  await vi.advanceTimersByTimeAsync(10_000);
  const recovered = await tenantSession.refresh(request());
  expect(recovered.cookies.get('queueflow_refresh')?.value).toBe(pair.refreshToken);
  expect(fetch).toHaveBeenCalledTimes(1);
  jar.delete('queueflow_refresh_attempt');
  const replay = await tenantSession.refresh(request());
  expect(replay.status).toBe(503);
  expect(replay.headers.has('set-cookie')).toBe(false);
  expect(fetch).toHaveBeenCalledTimes(2);
});
