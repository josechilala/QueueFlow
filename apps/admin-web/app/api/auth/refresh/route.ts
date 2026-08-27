import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { apiUrl, REFRESH_COOKIE, type TokenPair } from '../../../../lib/auth';
import { clearAuthCookies, setAuthCookies } from '../../../../lib/auth-cookies';
import { publicUrl } from '../../../../lib/public-url';

export async function GET(request: Request) {
  const refreshToken = (await cookies()).get(REFRESH_COOKIE)?.value; const candidate = new URL(request.url).searchParams.get('returnTo') ?? '/dashboard';
  const returnTo = candidate.startsWith('/') && !candidate.startsWith('//') ? candidate : '/dashboard';
  if (!refreshToken) return clearAndRedirect(request);
  const apiResponse = await fetch(`${apiUrl}/api/v1/auth/refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }), cache: 'no-store' });
  if (!apiResponse.ok) return clearAndRedirect(request);
  const response = NextResponse.redirect(publicUrl(request, returnTo), 303); setAuthCookies(response, (await apiResponse.json()) as TokenPair); return response;
}

function clearAndRedirect(request: Request) { const response = NextResponse.redirect(publicUrl(request, '/login?reason=session_expired'), 303); clearAuthCookies(response); return response; }
