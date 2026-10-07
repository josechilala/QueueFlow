import { createHash } from 'node:crypto';
import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { retryDelay, safeReturnTo } from './recovery';
import { correlationId } from './correlation';

type TokenPair = { accessToken: string; refreshToken: string };
type Renewal = { kind: 'success'; tokens: TokenPair } | { kind: 'rejected' } | { kind: 'temporary'; status: number; retryAfter?: string; retryAt?: number; code?: string };
type Configuration = {
  apiUrl: () => string;
  accessCookie: string;
  refreshCookie: string;
  recoveryCookie?: string;
  refreshPath: string;
  rejectionCode: string;
  home: string;
  login: string;
  publicUrl: (request: Request, path: string) => URL;
  setCookies: (response: NextResponse, tokens: TokenPair) => void;
  clearCookies: (response: NextResponse) => void;
};

// Shared across route bundles in this process. Only hashes are used as keys.
// PostgreSQL also serializes rotations, so another BFF instance cannot rotate twice.
const state = globalThis as typeof globalThis & { queueflowRenewals?: Map<string, Promise<Renewal>> };
const renewals = state.queueflowRenewals ??= new Map<string, Promise<Renewal>>();

export async function sessionFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  const requestCorrelationId = correlationId(headers.get('X-Correlation-ID'));
  headers.set('X-Correlation-ID', requestCorrelationId);
  try {
    // Buffer inside the timeout/error boundary, including failures while reading the body.
    const response = await fetch(url, { ...init, headers, cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(30_000) });
    const body = response.status === 204 ? null : await response.arrayBuffer();
    return new Response(body, { status: response.status, headers: response.headers });
  } catch {
    return Response.json({ message: 'Serviço temporariamente indisponível. Tente novamente.' }, { status: 503, headers: { 'X-Correlation-ID': requestCorrelationId } });
  }
}

function temporary(status = 503, retryAfter?: string, code?: string, requestCorrelationId?: string): NextResponse {
  return NextResponse.json({ message: 'Não foi possível validar a sessão agora. Tente novamente.' , code }, {
    status, headers: { 'Cache-Control': 'no-store', ...(requestCorrelationId ? { 'X-Correlation-ID': requestCorrelationId } : {}), ...(retryAfter ? { 'Retry-After': retryAfter } : {}) },
  });
}

export function createSession(config: Configuration) {
  async function renew(refreshToken: string, requestCorrelationId: string): Promise<Renewal> {
    const proof = config.recoveryCookie ? (await cookies()).get(config.recoveryCookie)?.value : undefined;
    const key = createHash('sha256').update(JSON.stringify([config.apiUrl(), config.refreshPath, refreshToken, proof])).digest('hex');
    const existing = renewals.get(key);
    if (existing) {
      const result = await existing;
      if (result.kind !== 'temporary' || !result.retryAt || result.retryAt > Date.now()) return result;
      const current = renewals.get(key);
      if (current && current !== existing) return current;
      if (current === existing) renewals.delete(key);
    }
    // Bound memory without evicting active rotations and causing duplicate requests.
    if (renewals.size >= 1024) return { kind: 'temporary', status: 503 };
    const pending = (async (): Promise<Renewal> => {
      const startedAt = Date.now();
      const response = await sessionFetch(`${config.apiUrl()}${config.refreshPath}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': requestCorrelationId }, body: JSON.stringify({ refreshToken }),
      });
      const body = await response.json().catch(() => null);
      const replayCode = config.rejectionCode.replace('invalid_refresh', 'refresh_conflict');
      const outcome = response.ok ? 'success' : response.status === 409 && body?.code === replayCode ? 'refresh_replay'
        : response.status === 429 ? 'rate_limited' : response.status >= 500 ? 'api_unavailable' : 'refresh_rejected';
      console.info(JSON.stringify({ event: 'auth_operation', operation: 'refresh', outcome, status: response.status,
        durationMs: Date.now() - startedAt, correlationId: requestCorrelationId, source: 'api_refresh' }));
      if (response.ok && typeof body?.accessToken === 'string' && body.accessToken.trim() &&
          typeof body?.refreshToken === 'string' && body.refreshToken.trim()) return { kind: 'success', tokens: body };
      // A proxy-generated 401, malformed response, or rotation conflict is NOT a rejection.
      if (response.status === 401 && body?.code === config.rejectionCode) return { kind: 'rejected' };
      return { kind: 'temporary', status: response.status === 429 ? 429 : response.status >= 500 ? response.status : 503,
        retryAt: Date.now() + retryDelay(response.headers.get('Retry-After'), response.status === 429 ? 60 : 5) * 1000,
        code: response.status === 409 && body?.code === config.rejectionCode.replace('invalid_refresh', 'refresh_conflict') ? 'refresh_conflict' : undefined };
    })();
    renewals.set(key, pending);
    const result = await pending;
    if (result.kind === 'success') {
      // Admin recovery requires a separate HttpOnly browser proof established BEFORE
      // rotation. A stolen/replayed refresh token alone cannot retrieve this result.
      // No API replay grace: process loss still requires a fresh login.
      const retention = config.recoveryCookie ? (proof ? 60_000 : 0) : 5_000;
      if (retention) {
        const timer = setTimeout(() => { if (renewals.get(key) === pending) renewals.delete(key); }, retention);
        timer.unref();
      } else if (renewals.get(key) === pending) renewals.delete(key);
    } else if (result.kind === 'temporary') {
      console.warn(JSON.stringify({ event: 'session_refresh_temporary', status: result.status, code: result.code ?? 'upstream_unavailable', endpoint: config.refreshPath, correlationId: requestCorrelationId }));
      const timer = setTimeout(() => { if (renewals.get(key) === pending) renewals.delete(key); }, Math.min(2_147_483_647, Math.max(0, (result.retryAt ?? Date.now()) - Date.now())));
      timer.unref();
    } else if (renewals.get(key) === pending) renewals.delete(key);
    return result;
  }

  async function refresh(request: Request): Promise<NextResponse> {
    const startedAt = Date.now();
    const destination = safeReturnTo(new URL(request.url).searchParams.get('returnTo'), config.home);
    const requestId = correlationId(request.headers.get('X-Correlation-ID'));
    const navigate = (path: string) => {
      const response = request.method === 'POST'
      ? NextResponse.json({ redirectTo: path }, { headers: { 'Cache-Control': 'no-store' } })
      : NextResponse.redirect(config.publicUrl(request, path), 303);
      response.headers.set('X-Correlation-ID', requestId);
      return response;
    };
    const token = (await cookies()).get(config.refreshCookie)?.value;
    if (!token) return navigate(`${config.login}?returnTo=${encodeURIComponent(destination)}`);
    const result = await renew(token, requestId);
    console.info(JSON.stringify({ event: 'auth_operation', operation: 'session_recovery',
      outcome: result.kind === 'success' ? 'success' : result.kind === 'rejected' ? 'refresh_rejected' : 'temporarily_unavailable',
      status: result.kind === 'success' ? 200 : result.kind === 'rejected' ? 401 : result.status,
      durationMs: Date.now() - startedAt, correlationId: requestId, source: 'bff_recovery' }));
    if (result.kind === 'temporary') return temporary(result.status, result.retryAt ? String(Math.max(0, Math.ceil((result.retryAt - Date.now()) / 1000))) : result.retryAfter, result.code, requestId);
    if (result.kind === 'rejected') {
      const response = navigate(`${config.login}?reason=session_expired&returnTo=${encodeURIComponent(destination)}`);
      response.headers.set('Cache-Control', 'no-store');
      config.clearCookies(response);
      return response;
    }
    const response = navigate(destination);
    response.headers.set('Cache-Control', 'no-store');
    config.setCookies(response, result.tokens);
    return response;
  }

  // Only route handlers use this: server components cannot persist rotated cookies.
  async function forward(path: string, init: RequestInit = {}): Promise<NextResponse> {
    const jar = await cookies();
    const access = jar.get(config.accessCookie)?.value;
    const refreshToken = jar.get(config.refreshCookie)?.value;
    const requestId = correlationId(new Headers(init.headers).get('X-Correlation-ID'));
    const send = (token: string) => {
      const headers = new Headers(init.headers);
      headers.set('X-Correlation-ID', requestId);
      headers.set('Authorization', `Bearer ${token}`);
      return sessionFetch(`${config.apiUrl()}${path}`, { ...init, headers });
    };
    let upstream = access ? await send(access) : new Response(null, { status: 401 });
    let rotated: TokenPair | undefined;
    if (upstream.status === 401 && refreshToken) {
      const result = await renew(refreshToken, requestId);
      if (result.kind === 'temporary') return temporary(result.status, result.retryAt ? String(Math.max(0, Math.ceil((result.retryAt - Date.now()) / 1000))) : result.retryAfter, result.code, requestId);
      if (result.kind === 'rejected') {
        const response = NextResponse.json({ message: 'Sessão expirada.' }, { status: 401, headers: { 'Cache-Control': 'no-store', 'X-Correlation-ID': requestId } });
        config.clearCookies(response);
        return response;
      }
      rotated = result.tokens;
      upstream = await send(rotated.accessToken);
      // Fresh credentials rejected by another instance can indicate configuration drift.
      if (upstream.status === 401) upstream = temporary(503, undefined, undefined, requestId);
    }
    const headers = new Headers({ 'Cache-Control': 'no-store', 'Content-Type': upstream.headers.get('Content-Type') ?? 'application/json' });
    for (const name of ['Retry-After', 'X-Correlation-ID']) {
      const value = upstream.headers.get(name);
      if (value) headers.set(name, value);
    }
    const response = new NextResponse(upstream.status === 204 ? null : await upstream.text(), { status: upstream.status, headers });
    if (rotated) config.setCookies(response, rotated);
    return response;
  }

  return { refresh, forward };
}
