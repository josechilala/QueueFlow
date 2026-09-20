import { createHash } from 'node:crypto';
import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';

type TokenPair = { accessToken: string; refreshToken: string };
type Renewal = { kind: 'success'; tokens: TokenPair } | { kind: 'rejected' } | { kind: 'temporary'; status: number; retryAfter?: string };
type Configuration = {
  apiUrl: () => string;
  accessCookie: string;
  refreshCookie: string;
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
  try {
    // Buffer inside the timeout/error boundary, including failures while reading the body.
    const response = await fetch(url, { ...init, cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(30_000) });
    const body = response.status === 204 ? null : await response.arrayBuffer();
    return new Response(body, { status: response.status, headers: response.headers });
  } catch {
    return Response.json({ message: 'Serviço temporariamente indisponível. Tente novamente.' }, { status: 503 });
  }
}

function temporary(status = 503, retryAfter?: string): NextResponse {
  return NextResponse.json({ message: 'Não foi possível validar a sessão agora. Tente novamente.' }, {
    status, headers: { 'Cache-Control': 'no-store', ...(retryAfter ? { 'Retry-After': retryAfter } : {}) },
  });
}

export function createSession(config: Configuration) {
  async function renew(refreshToken: string): Promise<Renewal> {
    const key = createHash('sha256').update(JSON.stringify([config.apiUrl(), config.refreshPath, refreshToken])).digest('hex');
    const existing = renewals.get(key);
    if (existing) return existing;
    // Bound memory without evicting active rotations and causing duplicate requests.
    if (renewals.size >= 1024) return { kind: 'temporary', status: 503 };
    const pending = (async (): Promise<Renewal> => {
      const response = await sessionFetch(`${config.apiUrl()}${config.refreshPath}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }),
      });
      const body = await response.json().catch(() => null);
      if (response.ok && typeof body?.accessToken === 'string' && body.accessToken.trim() &&
          typeof body?.refreshToken === 'string' && body.refreshToken.trim()) return { kind: 'success', tokens: body };
      // A proxy-generated 401, malformed response, or rotation conflict is NOT a rejection.
      if (response.status === 401 && body?.code === config.rejectionCode) return { kind: 'rejected' };
      return { kind: 'temporary', status: response.status === 429 ? 429 : response.status >= 500 ? response.status : 503,
        retryAfter: response.headers.get('Retry-After') ?? undefined };
    })();
    renewals.set(key, pending);
    const result = await pending;
    if (result.kind === 'success') {
      // Requests already carrying the old cookie receive the same pair, not another rotation.
      const timer = setTimeout(() => { if (renewals.get(key) === pending) renewals.delete(key); }, 5_000);
      timer.unref();
    } else if (renewals.get(key) === pending) renewals.delete(key);
    return result;
  }

  async function refresh(request: Request): Promise<NextResponse> {
    const token = (await cookies()).get(config.refreshCookie)?.value;
    if (!token) return NextResponse.redirect(config.publicUrl(request, config.login), 303);
    const result = await renew(token);
    if (result.kind === 'temporary') return temporary(result.status, result.retryAfter);
    if (result.kind === 'rejected') {
      const response = NextResponse.redirect(config.publicUrl(request, `${config.login}?reason=session_expired`), 303);
      response.headers.set('Cache-Control', 'no-store');
      config.clearCookies(response);
      return response;
    }
    const origin = config.publicUrl(request, '/');
    const candidate = new URL(request.url).searchParams.get('returnTo') ?? config.home;
    let destination = config.publicUrl(request, config.home);
    if (candidate.startsWith('/') && !candidate.startsWith('//')) {
      // URL parsing also rejects backslash-based cross-origin redirects.
      try {
        const parsed = new URL(candidate, origin);
        if (parsed.origin === origin.origin && !parsed.pathname.startsWith('/api/')) destination = parsed;
      } catch { /* Keep the safe default for malformed destinations. */ }
    }
    const response = NextResponse.redirect(destination, 303);
    response.headers.set('Cache-Control', 'no-store');
    config.setCookies(response, result.tokens);
    return response;
  }

  // Only route handlers use this: server components cannot persist rotated cookies.
  async function forward(path: string, init: RequestInit = {}): Promise<NextResponse> {
    const jar = await cookies();
    const access = jar.get(config.accessCookie)?.value;
    const refreshToken = jar.get(config.refreshCookie)?.value;
    const send = (token: string) => {
      const headers = new Headers(init.headers);
      headers.set('Authorization', `Bearer ${token}`);
      return sessionFetch(`${config.apiUrl()}${path}`, { ...init, headers });
    };
    let upstream = access ? await send(access) : new Response(null, { status: 401 });
    let rotated: TokenPair | undefined;
    if (upstream.status === 401 && refreshToken) {
      const result = await renew(refreshToken);
      if (result.kind === 'temporary') return temporary(result.status, result.retryAfter);
      if (result.kind === 'rejected') {
        const response = NextResponse.json({ message: 'Sessão expirada.' }, { status: 401, headers: { 'Cache-Control': 'no-store' } });
        config.clearCookies(response);
        return response;
      }
      rotated = result.tokens;
      upstream = await send(rotated.accessToken);
      // Fresh credentials rejected by another instance can indicate configuration drift.
      if (upstream.status === 401) upstream = temporary();
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
