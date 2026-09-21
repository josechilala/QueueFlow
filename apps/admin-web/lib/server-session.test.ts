import { afterEach, expect, it, vi } from 'vitest';
import { getServerSession } from './server-session';
import { getDashboardSummary } from './server-dashboard';
import { getOnboardingProgress } from './server-onboarding';
vi.mock('next/headers', () => ({ cookies: vi.fn(async () => ({ get: () => ({ value: 'access' }) })) }));
afterEach(() => vi.unstubAllGlobals());
it.each([new TypeError('network'), new DOMException('timeout', 'TimeoutError')])('uses the common transport on dashboard and onboarding: %s', async error => {
  const fetch = vi.fn().mockRejectedValue(error);
  vi.stubGlobal('fetch', fetch);
  expect(await getDashboardSummary()).toEqual({ status: 503 });
  await expect(getOnboardingProgress()).rejects.toThrow();
  expect(await getServerSession()).toEqual({ status: 503, retryAfter: undefined });
  for (const [, init] of fetch.mock.calls) {
    expect(init.cache).toBe('no-store');
    expect(init.redirect).toBe('error');
    expect(init.signal).toBeInstanceOf(AbortSignal);
  }
});
it('preserves upstream throttling during session validation', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 429, headers: { 'Retry-After': '40' } })));
  expect(await getServerSession()).toEqual({ status: 429, retryAfter: '40' });
});
