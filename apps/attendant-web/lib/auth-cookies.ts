import type { NextResponse } from 'next/server';
import { ACCESS_COOKIE, type TokenPair } from './auth';
const secure = process.env.QUEUEFLOW_SECURE_COOKIES === 'true' || (process.env.NODE_ENV === 'production' && process.env.QUEUEFLOW_SECURE_COOKIES !== 'false');
export function setAuthCookie(response: NextResponse, tokens: TokenPair) { response.cookies.set(ACCESS_COOKIE, tokens.accessToken, { httpOnly: true, sameSite: 'lax', secure, path: '/', maxAge: 15 * 60 }); }
export function clearAuthCookie(response: NextResponse) {
  const options = { httpOnly: true, sameSite: 'lax' as const, secure, path: '/', maxAge: 0 };
  response.cookies.set(ACCESS_COOKIE, '', options);
  response.cookies.set('queueflow_access', '', options);
  response.cookies.set('queueflow_refresh', '', options);
}
