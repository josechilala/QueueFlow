import type { NextResponse } from 'next/server';
import { ACCESS_COOKIE, REFRESH_COOKIE, type TokenPair } from './auth';
const secure = process.env.QUEUEFLOW_SECURE_COOKIES === 'true' || (process.env.NODE_ENV === 'production' && process.env.QUEUEFLOW_SECURE_COOKIES !== 'false');
export function setAuthCookie(response: NextResponse, tokens: TokenPair) {
  const options = { httpOnly: true, sameSite: 'lax' as const, secure, path: '/' };
  response.cookies.set(ACCESS_COOKIE, tokens.accessToken, { ...options, maxAge: 15 * 60 });
  response.cookies.set(REFRESH_COOKIE, tokens.refreshToken, { ...options, maxAge: 30 * 24 * 60 * 60 });
}
export function clearAuthCookie(response: NextResponse) {
  const options = { httpOnly: true, sameSite: 'lax' as const, secure, path: '/', maxAge: 0 };
  response.cookies.set(ACCESS_COOKIE, '', options);
  response.cookies.set(REFRESH_COOKIE, '', options);
}
