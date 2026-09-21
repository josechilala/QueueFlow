import { retryDelay, safeReturnTo } from '../../../packages/session/recovery';

type Options = { signal: AbortSignal; onWait: (seconds: number) => void; onNavigate: (path: string) => void };
const cooldownKey = 'queueflow.admin.refresh.retryAt';
let localRetryAt = 0;
function deadline(): number {
  try { return Math.max(localRetryAt, Number(localStorage.getItem(cooldownKey)) || 0); } catch { return localRetryAt; }
}
function defer(seconds: number) {
  localRetryAt = Math.max(deadline(), Date.now() + seconds * 1000);
  try { localStorage.setItem(cooldownKey, String(localRetryAt)); } catch { /* Private browsing still has a per-tab cooldown. */ }
}
async function wait(options: Options) {
  while (!options.signal.aborted && deadline() > Date.now()) {
    options.onWait(Math.ceil((deadline() - Date.now()) / 1000));
    await new Promise<void>(resolve => {
      const done = () => { clearTimeout(timer); options.signal.removeEventListener('abort', done); resolve(); };
      const timer = setTimeout(done, Math.min(1000, deadline() - Date.now()));
      options.signal.addEventListener('abort', done, { once: true });
    });
  }
}
export async function recoverSession(returnTo: string, options: Options): Promise<'navigated' | 'unavailable' | 'conflict'> {
  const destination = safeReturnTo(returnTo);
  for (let attempt = 0; attempt < 3 && !options.signal.aborted; attempt++) {
    const run = async () => {
      await wait(options);
      if (options.signal.aborted) return null;
      try {
        // Do not cancel a rotation when the component unmounts: let Set-Cookie
        // arrive and retain the browser lock until the response is consumed.
        // Budget covers /me + refresh (30 seconds each) and response delivery.
        const response = await fetch(`/api/auth/refresh?returnTo=${encodeURIComponent(destination)}`, {
          method: 'POST', cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(70_000),
        });
        const body = await response.json().catch(() => null);
        if (response.ok && typeof body?.redirectTo === 'string') {
          const path = body.redirectTo.startsWith('/login?') ? body.redirectTo : safeReturnTo(body.redirectTo);
          return { path };
        }
        if (body?.code === 'refresh_conflict') return { conflict: true };
        defer(Math.max(5 * 2 ** attempt, retryDelay(response.headers.get('Retry-After'), response.status === 429 ? 60 : 5)));
        return null;
      } catch {
        defer(5 * 2 ** attempt);
        return null;
      }
    };
    // Coordinates tabs without putting any credentials in browser storage.
    const result = navigator.locks
      ? await navigator.locks.request('queueflow-admin-session', { signal: options.signal }, run)
      : await run();
    if (options.signal.aborted) return 'unavailable';
    if (result && 'path' in result) { options.onNavigate(result.path!); return 'navigated'; }
    if (result && 'conflict' in result) return 'conflict';
  }
  return 'unavailable';
}
