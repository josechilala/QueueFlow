import { NextResponse } from 'next/server';
import { apiUrl, type AuthenticatedUser, type TokenPair } from '../../../../lib/auth';
import { setAuthCookies } from '../../../../lib/auth-cookies';
import { clientIpHeaders } from '../../../../lib/trusted-client-ip.mjs';
import type { OnboardingProgress } from '../../../../lib/server-onboarding';
import { loginError } from '../../../../lib/login-feedback';

function failure(status: number, upstream?: Response) {
  const headers = new Headers({ 'Cache-Control': 'no-store' });
  const retryAfter = upstream?.headers.get('Retry-After');
  if (retryAfter) headers.set('Retry-After', retryAfter);
  const policy = upstream?.headers.get('X-RateLimit-Policy');
  if (status === 429 && policy && ['auth', 'global', 'refresh'].includes(policy)) headers.set('X-RateLimit-Policy', policy);
  return NextResponse.json({ message: loginError(status) }, { status, headers });
}

export async function POST(request: Request) {
  let body;
  try { body = await request.json(); } catch { return failure(400); }
  if (typeof body?.email !== 'string' || !body.email.trim() || typeof body?.password !== 'string' || !body.password) return failure(400);
  if (!apiUrl) return failure(503);
  // One deadline includes the POST, profile reads and response bodies. No retries.
  const deadline = AbortSignal.timeout(75_000);
  const signal = AbortSignal.any([deadline, request.signal]);
  const options = { cache: 'no-store', redirect: 'error', signal } as const;
  try {
    const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, { ...options, method: 'POST', headers: { 'Content-Type': 'application/json', ...clientIpHeaders(request.headers) }, body: JSON.stringify({ email: body.email, password: body.password }) });
    if (!apiResponse.ok) return failure(apiResponse.status >= 400 ? apiResponse.status : 502, apiResponse);
    const tokens = (await apiResponse.json()) as TokenPair;
    if (!tokens || typeof tokens.accessToken !== 'string' || !tokens.accessToken.trim() || typeof tokens.refreshToken !== 'string' || !tokens.refreshToken.trim()) return failure(502);
    const sessionResponse = await fetch(`${apiUrl}/api/v1/auth/me`, { ...options, headers: { Authorization: `Bearer ${tokens.accessToken}` } });
    if (!sessionResponse.ok) return failure(sessionResponse.status === 429 || sessionResponse.status >= 500 ? sessionResponse.status : 502, sessionResponse);
    const session = (await sessionResponse.json()) as AuthenticatedUser;
    if (!session || !['Owner', 'Admin', 'Manager', 'Attendant', 'Viewer'].includes(session.role)) return failure(502);
    let needsOnboarding = false;
    if (session.role === 'Owner') {
      const progress = await fetch(`${apiUrl}/api/v1/onboarding`, { ...options, headers: { Authorization: `Bearer ${tokens.accessToken}` } });
      if (!progress.ok) return failure(progress.status === 429 || progress.status >= 500 ? progress.status : 502, progress);
      const configuration = await progress.json() as OnboardingProgress;
      if (!configuration || ['completed', 'branchReady', 'servicesReady', 'operationReady'].some(key => typeof configuration[key as keyof OnboardingProgress] !== 'boolean')) return failure(502);
      // Completed is the explicit acknowledgement; existing operations may already
      // be ready without it. Read readiness from the same API as /onboarding.
      needsOnboarding = !(configuration.completed ||
        (configuration.branchReady && configuration.servicesReady && configuration.operationReady));
      if (!configuration.completed && !needsOnboarding) {
        // Persist readiness through the existing backend validation before bypassing
        // onboarding. This also reconciles operational legacy organizations safely.
        const completion = await fetch(`${apiUrl}/api/v1/onboarding/complete`, {
          ...options, method: 'POST', headers: { Authorization: `Bearer ${tokens.accessToken}` },
        });
        if (completion.status === 400) needsOnboarding = true; // Configuration changed since the read.
        else if (!completion.ok) return failure(completion.status === 429 || completion.status >= 500 ? completion.status : 502, completion);
      }
    }
    const response = NextResponse.json({ authenticated: true, role: session.role, needsOnboarding }, { headers: { 'Cache-Control': 'no-store' } });
    if (session.role !== 'Attendant') setAuthCookies(response, tokens);
    return response;
  } catch {
    // Failures never clear or replace existing session cookies.
    return failure(deadline.aborted ? 504 : 502);
  }
}
