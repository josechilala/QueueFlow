import type { NextResponse } from 'next/server';
import { PLATFORM_ACCESS_COOKIE, PLATFORM_REFRESH_COOKIE, type TokenPair } from './platform-auth';

const secure = process.env.QUEUEFLOW_SECURE_COOKIES === 'true' || (process.env.NODE_ENV === 'production' && process.env.QUEUEFLOW_SECURE_COOKIES !== 'false');
const options = { httpOnly: true, sameSite: 'lax' as const, secure, path: '/' };
export function setPlatformAuthCookies(response: NextResponse, tokens: TokenPair) { response.cookies.set(PLATFORM_ACCESS_COOKIE, tokens.accessToken, { ...options, maxAge: 15 * 60 }); response.cookies.set(PLATFORM_REFRESH_COOKIE, tokens.refreshToken, { ...options, maxAge: 30 * 24 * 60 * 60 }); }
export function clearPlatformAuthCookies(response: NextResponse) { response.cookies.set(PLATFORM_ACCESS_COOKIE, '', { ...options, maxAge: 0 }); response.cookies.set(PLATFORM_REFRESH_COOKIE, '', { ...options, maxAge: 0 }); return response; }
