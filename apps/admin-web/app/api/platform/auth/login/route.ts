import { NextResponse } from 'next/server';
import { setPlatformAuthCookies } from '../../../../../lib/platform-auth-cookies';
import { configuredUrl } from '../../../../../lib/configured-url';
import { clientIpHeaders } from '../../../../../lib/trusted-client-ip.mjs';
import { platformLoginError } from '../../../../../lib/platform-login';

function failure(status: number, retryAfter?: string | null) {
  return NextResponse.json({ message: platformLoginError(status) }, {
    status, headers: { 'Cache-Control': 'no-store', ...(retryAfter ? { 'Retry-After': retryAfter } : {}) },
  });
}

export async function POST(request: Request) {
  let body;
  try { body = await request.json(); } catch { return failure(400); }
  if (typeof body?.email !== 'string' || !body.email.trim() || typeof body?.password !== 'string' || !body.password) return failure(400);

  // Same configuration source as tenant login; resolve at runtime, without a production fallback.
  const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  if (!apiUrl) return failure(503);
  const origin = new URL(apiUrl);
  if (origin.pathname !== '/' || origin.search || origin.hash) return failure(503);

  try {
    const response = await fetch(`${apiUrl}/api/v1/platform/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', ...clientIpHeaders(request.headers) },
      body: JSON.stringify({ email: body.email, password: body.password }), cache: 'no-store',
      redirect: 'error', signal: AbortSignal.timeout(30_000),
    });
    if (!response.ok) return failure(response.status >= 400 ? response.status : 502, response.headers.get('Retry-After'));
    const tokens = await response.json();
    if (typeof tokens?.accessToken !== 'string' || !tokens.accessToken.trim() ||
        typeof tokens?.refreshToken !== 'string' || !tokens.refreshToken.trim()) return failure(502);
    const result = NextResponse.json({ authenticated: true }, { headers: { 'Cache-Control': 'no-store' } });
    setPlatformAuthCookies(result, tokens);
    return result;
  } catch {
    // Network, timeout, redirect and malformed upstream responses are service failures, not bad credentials.
    return failure(502);
  }
}
