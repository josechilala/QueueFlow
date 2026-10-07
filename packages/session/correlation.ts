export function correlationId(value?: string | null): string {
  if (value && value.length <= 64 && /^[A-Za-z0-9_-]+$/.test(value)) return value;
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID().replaceAll('-', '');
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
}
