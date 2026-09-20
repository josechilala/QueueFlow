import { createSession } from '../../../packages/session/server';
import { ACCESS_COOKIE, REFRESH_COOKIE } from './auth';
import { setAuthCookies, clearAuthCookies } from './auth-cookies';
import { PLATFORM_ACCESS_COOKIE, PLATFORM_REFRESH_COOKIE } from './platform-auth';
import { setPlatformAuthCookies, clearPlatformAuthCookies } from './platform-auth-cookies';
import { configuredUrl } from './configured-url';
import { publicUrl } from './public-url';

const apiUrl = () => configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
export const tenantSession = createSession({ apiUrl, publicUrl, accessCookie: ACCESS_COOKIE, refreshCookie: REFRESH_COOKIE,
  refreshPath: '/api/v1/auth/refresh', rejectionCode: 'auth.invalid_refresh', home: '/dashboard', login: '/login',
  setCookies: setAuthCookies, clearCookies: clearAuthCookies });
export const platformSession = createSession({ apiUrl, publicUrl, accessCookie: PLATFORM_ACCESS_COOKIE, refreshCookie: PLATFORM_REFRESH_COOKIE,
  refreshPath: '/api/v1/platform/auth/refresh', rejectionCode: 'platform.invalid_refresh', home: '/platform', login: '/platform/login',
  setCookies: setPlatformAuthCookies, clearCookies: clearPlatformAuthCookies });
