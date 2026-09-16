export function configuredUrl(value: string | undefined, developmentDefault = '', publicFacing = false): string {
  const raw = value?.trim() || (process.env.NODE_ENV === 'production' ? '' : developmentDefault);
  if (!raw) return '';
  try {
    const url = new URL(raw);
    if (url.username || url.password || !['http:', 'https:'].includes(url.protocol)) return '';
    if (publicFacing && process.env.NODE_ENV === 'production' && (url.protocol !== 'https:' || ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))) return '';
    return raw.replace(/\/+$/, '');
  } catch { return ''; }
}
