import { createSession } from '../../../packages/session/server';
import { ACCESS_COOKIE, REFRESH_COOKIE } from './auth';
import { setAuthCookie, clearAuthCookie } from './auth-cookies';
import { configuredUrl } from './configured-url';
import { publicUrl } from './public-url';

export const attendantSession = createSession({
  apiUrl: () => configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260'), publicUrl,
  accessCookie: ACCESS_COOKIE, refreshCookie: REFRESH_COOKIE, refreshPath: '/api/v1/auth/refresh',
  rejectionCode: 'auth.invalid_refresh', home: '/workstation', login: '/login',
  setCookies: setAuthCookie, clearCookies: clearAuthCookie,
});
