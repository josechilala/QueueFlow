import React from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { NextRequest } from 'next/server';
import { redirect } from 'next/navigation';
import { proxy } from '../proxy';
import { proxy as attendantProxy } from '../../attendant-web/proxy';
import PlatformLayout from '../app/platform/(protected)/layout';
import { getPlatformSession } from './server-platform';

vi.mock('next/navigation', () => ({ redirect: vi.fn(() => { throw new Error('redirect'); }) }));
vi.mock('./server-platform', () => ({ getPlatformSession: vi.fn() }));
afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals(); vi.unstubAllEnvs(); });

it.each([
  [proxy, '/dashboard', 'queueflow_refresh', '/api/auth/refresh'],
  [proxy, '/platform/invitations', 'queueflow_platform_refresh', '/api/platform/auth/refresh'],
  [attendantProxy, '/workstation', 'queueflow_attendant_refresh', '/api/auth/refresh'],
] as const)('routes expired access to refresh while retaining the destination', (run, path, cookie, refreshPath) => {
  vi.stubEnv('QUEUEFLOW_PUBLIC_URL', 'https://app.example.test');
  const result = run(new NextRequest(`https://app.example.test${path}?page=2`, { headers: { cookie: `${cookie}=valid-refresh` } }));
  const location = new URL(result.headers.get('location')!);
  expect(location.pathname).toBe(refreshPath);
  expect(location.searchParams.get('returnTo')).toBe(`${path}?page=2`);
  expect(result.headers.has('set-cookie')).toBe(false);
});

it.each([429, 500, 502, 503, 504])('Platform layout shows a recoverable error, not login, on %i', async status => {
  vi.mocked(getPlatformSession).mockResolvedValue({ status });
  await expect(PlatformLayout({ children: null })).rejects.toThrow('Tente novamente');
  expect(redirect).not.toHaveBeenCalled();
});

it('Platform layout renews on 401', async () => {
  vi.mocked(getPlatformSession).mockResolvedValue({ status: 401 });
  await expect(PlatformLayout({ children: null })).rejects.toThrow('redirect');
  expect(redirect).toHaveBeenCalledWith('/api/platform/auth/refresh');
});

it('Platform layout denies 403 without logging out', async () => {
  vi.stubGlobal('React', React);
  vi.mocked(getPlatformSession).mockResolvedValue({ status: 403 });
  const result = await PlatformLayout({ children: null });
  expect(result.type).toBe('main');
  expect(redirect).not.toHaveBeenCalled();
});
