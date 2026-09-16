import { publicUrl } from '../../../../../lib/public-url';
import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { apiUrl, PLATFORM_REFRESH_COOKIE, type TokenPair } from '../../../../../lib/platform-auth';
import { clearPlatformAuthCookies, setPlatformAuthCookies } from '../../../../../lib/platform-auth-cookies';

export async function GET(request: Request) { const refreshToken = (await cookies()).get(PLATFORM_REFRESH_COOKIE)?.value; if (!refreshToken) return failed(request); const apiResponse = await fetch(`${apiUrl}/api/v1/platform/auth/refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }), cache: 'no-store' }); if (!apiResponse.ok) return failed(request); const response = NextResponse.redirect(publicUrl(request, '/platform'), 303); setPlatformAuthCookies(response, await apiResponse.json() as TokenPair); return response; }
function failed(request: Request) { const response = NextResponse.redirect(publicUrl(request, '/platform/login?reason=session_expired'), 303); return clearPlatformAuthCookies(response); }
