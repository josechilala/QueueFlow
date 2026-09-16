import { afterEach, describe, expect, it, vi } from 'vitest';
import { NextResponse } from 'next/server';
import { clearAuthCookies } from './auth-cookies';
import { clearPlatformAuthCookies } from './platform-auth-cookies';
import { configuredUrl } from './configured-url';

afterEach(() => vi.unstubAllEnvs());
describe('independent sessions and configured origins', () => {
  it('tenant logout only expires tenant Admin cookies', () => {
    const response = NextResponse.json({});
    clearAuthCookies(response);
    expect(response.cookies.getAll().map(cookie => cookie.name).sort()).toEqual(['queueflow_access', 'queueflow_refresh']);
  });
  it('platform logout only expires platform cookies', () => {
    const response = NextResponse.json({});
    clearPlatformAuthCookies(response);
    expect(response.cookies.getAll().every(cookie => cookie.name.startsWith('queueflow_platform_'))).toBe(true);
  });
  it('production has no implicit development origin', () => {
    vi.stubEnv('NODE_ENV', 'production');
    expect(configuredUrl(undefined, 'http://localhost:5260')).toBe('');
    expect(configuredUrl('http://localhost:3003', '', true)).toBe('');
    expect(configuredUrl('https://attendant.example.test///', '', true)).toBe('https://attendant.example.test');
  });
});
