import { NextResponse } from 'next/server';
import { apiUrl, type AuthenticatedUser, type TokenPair } from '../../../../lib/auth';
import { setAuthCookies } from '../../../../lib/auth-cookies';
import { clientIpHeaders } from '../../../../lib/trusted-client-ip.mjs';
import { loginError } from '../../../../lib/login-feedback';

function failure(status: number, upstream?: Response, endpoint = '/api/v1/auth/login') {
  const headers = new Headers({ 'Cache-Control': 'no-store' });
  const retryAfter = upstream?.headers.get('Retry-After');
  if (retryAfter) headers.set('Retry-After', retryAfter);
  const policy = upstream?.headers.get('X-RateLimit-Policy');
  if (status === 429 && policy && ['auth', 'login', 'login-input', 'global', 'refresh'].includes(policy)) headers.set('X-RateLimit-Policy', policy);
  if (status === 429) {
    headers.set('X-RateLimit-Endpoint', endpoint);
    console.warn(JSON.stringify({ event: 'admin_login_rate_limited', endpoint,
      policy: headers.get('X-RateLimit-Policy') ?? 'unknown',
      retryAfter: retryAfter && /^\d{1,10}$/.test(retryAfter) ? Number(retryAfter) : null }));
  }
  return NextResponse.json({ message: loginError(status) }, { status, headers });
}

export async function POST(request: Request) {
  let body;
  try { body = await request.json(); } catch { return failure(400); }
  if (typeof body?.email !== 'string' || !body.email.trim() || typeof body?.password !== 'string' || !body.password) return failure(400);
  if (!apiUrl) return failure(503);

  // Authentication is the only responsibility of this BFF operation. Profile and
  // onboarding are post-login concerns and must never delay or invalidate a valid login.
  const deadline = AbortSignal.timeout(15_000);
  const signal = AbortSignal.any([deadline, request.signal]);
  try {
    const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, {
      method: 'POST', cache: 'no-store', redirect: 'error', signal,
      headers: { 'Content-Type': 'application/json', ...clientIpHeaders(request.headers) },
      body: JSON.stringify({ email: body.email, password: body.password }),
    });
    if (!apiResponse.ok) return failure(apiResponse.status >= 400 ? apiResponse.status : 502, apiResponse);
    const tokens = (await apiResponse.json()) as TokenPair;
    if (!tokens || typeof tokens.accessToken !== 'string' || !tokens.accessToken.trim() ||
        typeof tokens.refreshToken !== 'string' || !tokens.refreshToken.trim()) return failure(502);

    // Persist the authenticated session immediately. The dashboard/onboarding layer
    // resolves authorization context using the cookie-backed session after navigation.
    const response = NextResponse.json({ authenticated: true }, { headers: { 'Cache-Control': 'no-store' } });
    setAuthCookies(response, tokens);
    return response;
  } catch {
    return failure(deadline.aborted ? 504 : 502);
  }
}
