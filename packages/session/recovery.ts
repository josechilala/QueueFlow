// Shared by the BFF and browser; never accept an external or recursive destination.
export function safeReturnTo(value: string | null | undefined, fallback = '/dashboard'): string {
  if (!value?.startsWith('/') || value.startsWith('//')) return fallback;
  try {
    const url = new URL(value, 'https://session.invalid');
    if (url.origin !== 'https://session.invalid' || /^\/(api|session|login)(\/|$)/.test(url.pathname)) return fallback;
    return url.pathname + url.search + url.hash;
  } catch { return fallback; }
}

export function retryDelay(value: string | null, fallbackSeconds: number, now = Date.now()): number {
  if (!value) return fallbackSeconds;
  if (/^\d+$/.test(value)) {
    const seconds = Number(value);
    return Number.isSafeInteger(seconds) ? Math.max(1, seconds) : fallbackSeconds;
  }
  const deadline = Date.parse(value);
  return Number.isFinite(deadline) ? Math.max(1, Math.ceil((deadline - now) / 1000)) : fallbackSeconds;
}
