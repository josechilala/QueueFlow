import type { NextResponse } from 'next/server';
import { ACCESS_COOKIE, REFRESH_COOKIE, type TokenPair } from './auth';

const secure = process.env.QUEUEFLOW_SECURE_COOKIES === 'true' || (process.env.NODE_ENV === 'production' && process.env.QUEUEFLOW_SECURE_COOKIES !== 'false');
const options = { httpOnly: true, sameSite: 'lax' as const, secure, path: '/' };

export function setAuthCookies(response: NextResponse, tokens: TokenPair) {
  response.cookies.set(ACCESS_COOKIE, tokens.accessToken, { ...options, maxAge: 15 * 60 });
  response.cookies.set(REFRESH_COOKIE, tokens.refreshToken, { ...options, maxAge: 30 * 24 * 60 * 60 });
}

export function clearAuthCookies(response: NextResponse) {
  response.cookies.set(ACCESS_COOKIE, '', { ...options, maxAge: 0 });
  response.cookies.set(REFRESH_COOKIE, '', { ...options, maxAge: 0 });
  response.cookies.set('queueflow_attendant_access', '', { ...options, maxAge: 0 });
}
