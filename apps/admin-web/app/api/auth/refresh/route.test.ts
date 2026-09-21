import { beforeEach, afterEach, expect, it, vi } from 'vitest';
import { GET, POST } from './route';
import { getServerSession } from '../../../../lib/server-session';
import { tenantSession } from '../../../../lib/session';
vi.mock('next/headers', () => ({ cookies: vi.fn(async () => ({ has: () => false })) }));
vi.mock('../../../../lib/server-session', () => ({ getServerSession: vi.fn() }));
vi.mock('../../../../lib/session', () => ({ tenantSession: { refresh: vi.fn() } }));
beforeEach(() => { vi.clearAllMocks(); vi.stubEnv('QUEUEFLOW_PUBLIC_URL', 'https://admin.test'); });
afterEach(() => vi.unstubAllEnvs());
const request = () => new Request('https://admin.test/api/auth/refresh?returnTo=%2Freports%3Fpage%3D2', { method: 'POST', headers: { Origin: 'https://admin.test' } });
it('sends navigation to the recovery UI without rotating on GET', async () => {
  const response = await GET(request());
  expect(response.status).toBe(303);
  expect(response.cookies.get('queueflow_refresh_attempt')).toMatchObject({ httpOnly: true, sameSite: 'strict', maxAge: 300 });
  expect(response.headers.get('location')).toBe('https://admin.test/session/recover?returnTo=%2Freports%3Fpage%3D2');
  expect(tenantSession.refresh).not.toHaveBeenCalled();
});
it('recognizes cookies written by another tab or a response with a lost body', async () => {
  vi.mocked(getServerSession).mockResolvedValue({ status: 200, user: { userId: 'user' } as never });
  const response = await POST(request());
  expect(await response.json()).toEqual({ redirectTo: '/reports?page=2' });
  expect(tenantSession.refresh).not.toHaveBeenCalled();
});
it.each([429, 502, 503])('preserves session and Retry-After when validation returns %i', async status => {
  vi.mocked(getServerSession).mockResolvedValue({ status, retryAfter: '42' });
  const response = await POST(request());
  expect(response.status).toBe(status);
  expect(response.headers.get('retry-after')).toBe('42');
  expect(response.headers.has('set-cookie')).toBe(false);
  expect(tenantSession.refresh).not.toHaveBeenCalled();
});
it('renews only when validation returns 401', async () => {
  vi.mocked(getServerSession).mockResolvedValue({ status: 401 });
  vi.mocked(tenantSession.refresh).mockResolvedValue(Response.json({ redirectTo: '/reports?page=2' }) as never);
  expect((await POST(request())).status).toBe(200);
  expect(tenantSession.refresh).toHaveBeenCalledOnce();
});
it('rejects cross-origin refresh POSTs', async () => {
  expect((await POST(new Request('https://admin.test/api/auth/refresh', { method: 'POST', headers: { Origin: 'https://evil.test' } }))).status).toBe(403);
  expect(getServerSession).not.toHaveBeenCalled();
});
