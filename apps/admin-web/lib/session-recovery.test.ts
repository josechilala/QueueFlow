import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { retryDelay, safeReturnTo } from '../../../packages/session/recovery';

beforeEach(() => vi.resetModules());
afterEach(() => vi.unstubAllGlobals());

const setup = async (...responses: Array<Response | Error>) => {
  const fetch = vi.fn();
  responses.forEach(response => response instanceof Error ? fetch.mockRejectedValueOnce(response) : fetch.mockResolvedValueOnce(response));
  vi.stubGlobal('fetch', fetch);
  let queue = Promise.resolve();
  const request = vi.fn((_key, _options, run) => { const pending = queue.then(run); queue = pending.then(() => undefined); return pending; });
  vi.stubGlobal('navigator', { locks: { request } });
  const options = { signal: new AbortController().signal, onNavigate: vi.fn() };
  const { recoverSession } = await import('./session-recovery');
  return { recoverSession, options, fetch, request };
};

it('performs one refresh attempt and preserves the requested destination', async () => {
  const { recoverSession, options, fetch } = await setup(Response.json({ redirectTo: '/dashboard' }));
  expect(await recoverSession('/reports?from=2026-01-01', options)).toBe('navigated');
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(fetch.mock.calls[0][0]).toContain('returnTo=%2Freports%3Ffrom%3D2026-01-01');
  expect(options.onNavigate).toHaveBeenCalledWith('/reports?from=2026-01-01');
});

it.each([429, 502, 503])('does not create a browser retry loop after HTTP %i', async status => {
  const { recoverSession, options, fetch } = await setup(new Response(null, { status }));
  expect(await recoverSession('/dashboard', options)).toBe('unavailable');
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(options.onNavigate).not.toHaveBeenCalled();
});

it('does not retry a rotation conflict', async () => {
  const { recoverSession, options, fetch } = await setup(Response.json({ code: 'refresh_conflict' }, { status: 409 }));
  expect(await recoverSession('/dashboard', options)).toBe('conflict');
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(options.onNavigate).not.toHaveBeenCalled();
});

it('coordinates simultaneous callers with Web Locks', async () => {
  const { recoverSession, options, fetch, request } = await setup(Response.json({ redirectTo: '/dashboard' }), Response.json({ redirectTo: '/dashboard' }));
  const first = recoverSession('/dashboard', options);
  const second = recoverSession('/dashboard', options);
  expect(await first).toBe('navigated');
  expect(await second).toBe('navigated');
  expect(request).toHaveBeenCalledTimes(2);
  expect(fetch).toHaveBeenCalledTimes(2);
});

it('coalesces concurrent recovery without Web Locks and preserves each destination', async () => {
  const { recoverSession, options, fetch } = await setup();
  vi.stubGlobal('navigator', {});
  let complete!: (response: Response) => void;
  fetch.mockReturnValue(new Promise<Response>(resolve => { complete = resolve; }));
  const secondOptions = { ...options, onNavigate: vi.fn() };
  const first = recoverSession('/reports', options);
  const second = recoverSession('/dashboard', secondOptions);
  await Promise.resolve();
  expect(fetch).toHaveBeenCalledTimes(1);
  complete(Response.json({ redirectTo: '/reports' }));
  expect(await first).toBe('navigated');
  expect(await second).toBe('navigated');
  expect(options.onNavigate).toHaveBeenCalledWith('/reports');
  expect(secondOptions.onNavigate).toHaveBeenCalledWith('/dashboard');
});

it('treats Web Locks abort during StrictMode/unmount as lifecycle cancellation', async () => {
  const controller = new AbortController();
  const { recoverSession, options, fetch } = await setup();
  const request = vi.fn().mockImplementation(async () => {
    controller.abort();
    throw new DOMException('aborted', 'AbortError');
  });
  vi.stubGlobal('navigator', { locks: { request } });
  expect(await recoverSession('/dashboard', { ...options, signal: controller.signal })).toBe('unavailable');
  expect(fetch).not.toHaveBeenCalled();
});

it('a remount joins an unfinished renewal even after the first caller aborts', async () => {
  const { recoverSession, options, fetch } = await setup();
  vi.stubGlobal('navigator', {});
  let complete!: (response: Response) => void;
  fetch.mockReturnValue(new Promise<Response>(resolve => { complete = resolve; }));
  const controller = new AbortController();
  const first = recoverSession('/reports', { ...options, signal: controller.signal });
  await Promise.resolve();
  controller.abort();
  const second = recoverSession('/reports', options);
  expect(fetch).toHaveBeenCalledTimes(1);
  complete(Response.json({ redirectTo: '/reports' }));
  expect(await first).toBe('unavailable');
  expect(await second).toBe('navigated');
});

it('parses Retry-After dates and safely validates returnTo', () => {
  expect(retryDelay('invalid', 60)).toBe(60);
  for (const path of ['https://evil.test', '//evil.test', '/session/recover', '/api/auth/refresh', '/login']) expect(safeReturnTo(path)).toBe('/dashboard');
  expect(safeReturnTo('/reports?page=2')).toBe('/reports?page=2');
});
