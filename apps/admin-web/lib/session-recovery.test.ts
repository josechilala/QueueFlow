import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { retryDelay, safeReturnTo } from '../../../packages/session/recovery';

beforeEach(() => { vi.useFakeTimers(); vi.resetModules(); });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
const setup = async (...responses: Array<Response | Error>) => {
  const fetch = vi.fn();
  responses.forEach(response => response instanceof Error ? fetch.mockRejectedValueOnce(response) : fetch.mockResolvedValueOnce(response));
  vi.stubGlobal('fetch', fetch);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', { getItem: (key: string) => storage.get(key), setItem: (key: string, value: string) => storage.set(key, value) });
  let queue = Promise.resolve();
  const request = vi.fn((_key, _options, run) => { const pending = queue.then(run); queue = pending.then(() => undefined); return pending; });
  vi.stubGlobal('navigator', { locks: { request } });
  const options = { signal: new AbortController().signal, onWait: vi.fn(), onNavigate: vi.fn() };
  const { recoverSession } = await import('./session-recovery');
  return { recoverSession, options, fetch, request };
};

it('waits Retry-After before retrying 429 and preserves the original destination', async () => {
  const { recoverSession, options, fetch } = await setup(new Response(null, { status: 429, headers: { 'Retry-After': '60' } }), Response.json({ redirectTo: '/reports?from=2026-01-01' }));
  const pending = recoverSession('/reports?from=2026-01-01', options);
  await vi.advanceTimersByTimeAsync(59_000);
  expect(fetch).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(1_001);
  expect(await pending).toBe('navigated');
  expect(options.onNavigate).toHaveBeenCalledWith('/reports?from=2026-01-01');
  expect(fetch.mock.calls[0][0]).toContain('returnTo=%2Freports%3Ffrom%3D2026-01-01');
});

it.each([502, 503])('recovers after HTTP %i', async status => {
  const { recoverSession, options, fetch } = await setup(new Response(null, { status }), Response.json({ redirectTo: '/dashboard' }));
  const pending = recoverSession('/dashboard', options);
  await vi.advanceTimersByTimeAsync(5_001);
  expect(await pending).toBe('navigated');
  expect(fetch).toHaveBeenCalledTimes(2);
});

it('stops after three failures and keeps cooldown for a manual retry', async () => {
  const failures = Array.from({ length: 3 }, () => new Response(null, { status: 503 }));
  const { recoverSession, options, fetch } = await setup(...failures, Response.json({ redirectTo: '/dashboard' }));
  const pending = recoverSession('/dashboard', options);
  await vi.advanceTimersByTimeAsync(15_001);
  expect(await pending).toBe('unavailable');
  expect(fetch).toHaveBeenCalledTimes(3);
  const retry = recoverSession('/dashboard', options);
  await vi.advanceTimersByTimeAsync(19_000);
  expect(fetch).toHaveBeenCalledTimes(3);
  await vi.advanceTimersByTimeAsync(1_001);
  expect(await retry).toBe('navigated');
});

it('does not retry a rotation conflict and asks for explicit recovery', async () => {
  const { recoverSession, options, fetch } = await setup(Response.json({ code: 'refresh_conflict' }, { status: 503 }));
  expect(await recoverSession('/dashboard', options)).toBe('conflict');
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(options.onNavigate).not.toHaveBeenCalled();
});

it('coordinates simultaneous tabs and respects their shared cooldown', async () => {
  const { recoverSession, options, fetch, request } = await setup(new Response(null, { status: 429, headers: { 'Retry-After': '10' } }), Response.json({ redirectTo: '/dashboard' }), Response.json({ redirectTo: '/dashboard' }));
  const first = recoverSession('/dashboard', options);
  const second = recoverSession('/dashboard', options);
  await vi.advanceTimersByTimeAsync(9_000);
  expect(fetch).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(1_001);
  expect(await first).toBe('navigated'); expect(await second).toBe('navigated');
  expect(request).toHaveBeenCalled();
});

it('recovers after a lost response without an unlimited retry loop', async () => {
  const { recoverSession, options } = await setup(new TypeError('connection lost'), Response.json({ redirectTo: '/dashboard' }));
  const pending = recoverSession('/dashboard', options);
  await vi.advanceTimersByTimeAsync(5_001);
  expect(await pending).toBe('navigated');
});

it('parses Retry-After dates and safely validates returnTo', () => {
  expect(retryDelay(new Date(Date.now() + 30_000).toUTCString(), 5)).toBe(30);
  expect(retryDelay('invalid', 60)).toBe(60);
  for (const path of ['https://evil.test', '//evil.test', '/session/recover', '/api/auth/refresh', '/login']) expect(safeReturnTo(path)).toBe('/dashboard');
  expect(safeReturnTo('/reports?page=2')).toBe('/reports?page=2');
});

it('cancels waiting without sending another refresh', async () => {
  const { recoverSession, options, fetch } = await setup(new Response(null, { status: 429, headers: { 'Retry-After': '60' } }));
  const controller = new AbortController();
  const pending = recoverSession('/dashboard', { ...options, signal: controller.signal });
  await vi.advanceTimersByTimeAsync(1_000);
  controller.abort();
  expect(await pending).toBe('unavailable');
  expect(fetch).toHaveBeenCalledTimes(1);
});

it('retains a lock until a pending response is consumed after unmount', async () => {
  const { recoverSession, options, fetch } = await setup();
  let complete!: (response: Response) => void;
  fetch.mockReturnValue(new Promise<Response>(resolve => { complete = resolve; }));
  const controller = new AbortController();
  const pending = recoverSession('/dashboard', { ...options, signal: controller.signal });
  await vi.advanceTimersByTimeAsync(1);
  controller.abort();
  expect(fetch.mock.calls[0][1].signal.aborted).toBe(false);
  complete(Response.json({ redirectTo: '/dashboard' }));
  expect(await pending).toBe('unavailable');
  expect(options.onNavigate).not.toHaveBeenCalled();
});
