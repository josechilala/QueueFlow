import type { AuthenticatedUser } from './auth';

const allowedRoles: AuthenticatedUser['role'][] = ['Owner', 'Admin', 'Manager'];

export function canViewPublicSchedulingLink(role: AuthenticatedUser['role']) {
  return allowedRoles.includes(role);
}

export function buildPublicSchedulingUrl(baseUrl: string, organizationSlug: string) {
  const origin = baseUrl.trim().replace(/\/$/, '');
  const slug = organizationSlug.trim();
  if (!origin || !slug) return null;
  return `${origin}/empresa/${encodeURIComponent(slug)}`;
}

export async function copyPublicSchedulingUrl(url: string, clipboard: Pick<Clipboard, 'writeText'> = navigator.clipboard) {
  await clipboard.writeText(url);
}
