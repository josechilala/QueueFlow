import { NextResponse } from 'next/server';
import { apiUrl, type AuthenticatedUser, type TokenPair } from '../../../../lib/auth';
import { setAuthCookies } from '../../../../lib/auth-cookies';
import { clientIpHeaders } from '../../../../lib/trusted-client-ip.mjs';
import { loginError } from '../../../../lib/login-feedback';
import { correlationId } from '../../../../../../packages/session/correlation';

function failure(status: number, upstream?: Response, endpoint = '/api/v1/auth/login', requestId?: string) {
  const headers = new Headers({ 'Cache-Control': 'no-store' });
  if (requestId) headers.set('X-Correlation-ID', requestId);
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
  const requestId = correlationId(request.headers.get('X-Correlation-ID'));
  let body;
  try { body = await request.json(); } catch { return failure(400, undefined, undefined, requestId); }
  if (typeof body?.email !== 'string' || !body.email.trim() || typeof body?.password !== 'string' || !body.password) return failure(400, undefined, undefined, requestId);
  if (!apiUrl) return failure(503, undefined, undefined, requestId);

  // Keep onboarding out of the credential path, but resolve the authenticated role
  // before committing cookies so Attendants remain isolated from the admin session.
  const deadline = AbortSignal.timeout(15_000);
  const signal = AbortSignal.any([deadline, request.signal]);
  const options = { cache: 'no-store', redirect: 'error', signal } as const;
  try {
    const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, {
      ...options, method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': requestId, ...clientIpHeaders(request.headers) },
      body: JSON.stringify({ email: body.email, password: body.password }),
    });
    if (!apiResponse.ok) return failure(apiResponse.status >= 400 ? apiResponse.status : 502, apiResponse, undefined, requestId);
    const tokens = (await apiResponse.json()) as TokenPair;
    if (!tokens || typeof tokens.accessToken !== 'string' || !tokens.accessToken.trim() ||
        typeof tokens.refreshToken !== 'string' || !tokens.refreshToken.trim()) return failure(502, undefined, undefined, requestId);

    const sessionResponse = await fetch(`${apiUrl}/api/v1/auth/me`, {
      ...options, headers: { Authorization: `Bearer ${tokens.accessToken}`, 'X-Correlation-ID': requestId },
    });
    if (!sessionResponse.ok) return failure(
      sessionResponse.status === 429 || sessionResponse.status >= 500 ? sessionResponse.status : 502,
      sessionResponse,
      '/api/v1/auth/me',
      requestId,
    );
    const session = (await sessionResponse.json()) as AuthenticatedUser;
    if (!session || !['Owner', 'Admin', 'Manager', 'Attendant', 'Viewer'].includes(session.role)) return failure(502, undefined, undefined, requestId);

    const response = NextResponse.json({ authenticated: true, role: session.role }, { headers: { 'Cache-Control': 'no-store', 'X-Correlation-ID': requestId } });
    if (session.role !== 'Attendant') setAuthCookies(response, tokens);
    return response;
  } catch {
    return failure(deadline.aborted ? 504 : 502, undefined, undefined, requestId);
  }
}
