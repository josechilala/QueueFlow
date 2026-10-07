import { safeReturnTo } from '../../../packages/session/recovery';
import { correlationId } from '../../../packages/session/correlation';

type Options = { signal: AbortSignal; onNavigate: (path: string) => void };
type Attempt = { path: string } | { conflict: true } | null;
let inFlight: Promise<Attempt> | undefined;

export async function recoverSession(returnTo: string, options: Options): Promise<'navigated' | 'unavailable' | 'conflict'> {
  const destination = safeReturnTo(returnTo);
  const run = async (): Promise<Attempt> => {
    if (options.signal.aborted) return null;
    if (inFlight) return inFlight;
    const pending = (async (): Promise<Attempt> => {
      try {
        // Recovery is deliberately single-shot. Server-side refresh coordination is
        // authoritative; the browser must not create a second retry/cooldown policy.
        const response = await fetch(`/api/auth/refresh?returnTo=${encodeURIComponent(destination)}`, {
          method: 'POST', headers: { 'X-Correlation-ID': correlationId() }, cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(15_000),
        });
        const body = await response.json().catch(() => null);
        if (response.ok && typeof body?.redirectTo === 'string') {
          const path = body.redirectTo.startsWith('/login?') ? body.redirectTo : safeReturnTo(body.redirectTo);
          return { path };
        }
        if (body?.code === 'refresh_conflict') return { conflict: true };
        return null;
      } catch {
        return null;
      }
    })();
    inFlight = pending;
    try { return await pending; }
    finally { if (inFlight === pending) inFlight = undefined; }
  };

  let result: Attempt;
  try {
    result = navigator.locks
      ? await navigator.locks.request('queueflow-admin-session', { signal: options.signal }, run)
      : await run();
  } catch (error) {
    // Aborting while waiting for Web Locks is expected during navigation/StrictMode.
    // Treat only that lifecycle cancellation as unavailable; unexpected lock failures
    // still surface to the caller instead of being silently hidden.
    if (options.signal.aborted || (error instanceof DOMException && error.name === 'AbortError')) return 'unavailable';
    throw error;
  }

  if (options.signal.aborted) return 'unavailable';
  if (result && 'path' in result) {
    let path = destination;
    if (result.path.startsWith('/login?')) {
      const login = new URL(result.path, 'https://session.invalid');
      login.searchParams.set('returnTo', destination);
      path = login.pathname + login.search;
    }
    options.onNavigate(path);
    return 'navigated';
  }
  return result && 'conflict' in result ? 'conflict' : 'unavailable';
}
