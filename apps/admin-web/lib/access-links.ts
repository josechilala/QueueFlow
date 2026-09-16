import type { AuthenticatedUser } from './auth';

export type AccessLink = {
  id: 'attendant' | 'display' | 'customer' | 'scheduling';
  title: string;
  description: string;
  url: string | null;
};

export type AccessOrigins = {
  admin: string | null;
  attendant: string | null;
  customer: string | null;
  display: string | null;
};

type AccessEnvironment = Partial<Record<
  'QUEUEFLOW_PUBLIC_URL' |
  'QUEUEFLOW_CUSTOMER_URL' |
  'NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL' |
  'NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL',
  string
>>;

const localHosts = new Set(['localhost', '127.0.0.1', '::1', '[::1]']);
export const accessLinkAnchorProps = { target: '_blank', rel: 'noopener noreferrer' } as const;

export async function copyAccessLink(url: string, clipboard: Pick<Clipboard, 'writeText'>) {
  await clipboard.writeText(url);
}

export function canViewAccessLinks(role: AuthenticatedUser['role']) {
  return role === 'Owner' || role === 'Admin' || role === 'Manager';
}

export function normalizePublicOrigin(value: string | undefined, production = false): string | null {
  if (!value?.trim()) return null;
  try {
    const url = new URL(value.trim());
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || (production && (url.protocol !== 'https:' || localHosts.has(url.hostname)))) return null;
    return `${url.origin}${url.pathname.replace(/\/+$/, '')}`;
  } catch {
    return null;
  }
}

export function createAccessOrigins(environment: AccessEnvironment, production = false): AccessOrigins {
  return {
    admin: normalizePublicOrigin(environment.QUEUEFLOW_PUBLIC_URL, production),
    attendant: normalizePublicOrigin(environment.NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL, production),
    customer: normalizePublicOrigin(environment.QUEUEFLOW_CUSTOMER_URL, production),
    display: normalizePublicOrigin(environment.NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL, production),
  };
}

function append(origin: string | null, ...segments: string[]) {
  if (!origin || segments.some(segment => !segment.trim())) return null;
  return `${origin}/${segments.map(segment => encodeURIComponent(segment.trim())).join('/')}`;
}

export function getAttendantUrl(origins: AccessOrigins) {
  return origins.attendant;
}

export function getCustomerBranchUrl(origins: AccessOrigins, branchPublicId: string) {
  return append(origins.customer, 'unidade', branchPublicId);
}

export function getDisplayBranchUrl(origins: AccessOrigins, branchPublicId: string) {
  return append(origins.display, 'display', branchPublicId);
}

export function getPublicSchedulingUrl(origins: AccessOrigins, organizationSlug: string) {
  return append(origins.customer, 'agendamento', organizationSlug);
}

export function getOrganizationAccessLinks(origins: AccessOrigins, organizationSlug: string): AccessLink[] {
  return [
    {
      id: 'attendant',
      title: 'Painel do atendente',
      description: 'Envie este link para os usuários que irão realizar atendimentos.',
      url: getAttendantUrl(origins),
    },
    {
      id: 'scheduling',
      title: 'Agendamento público',
      description: 'Compartilhe este link no Instagram, WhatsApp ou site para o cliente agendar um horário.',
      url: getPublicSchedulingUrl(origins, organizationSlug),
    },
  ];
}

export function getBranchAccessLinks(origins: AccessOrigins, organizationSlug: string, branchPublicId: string): AccessLink[] {
  return [
    ...getOrganizationAccessLinks(origins, organizationSlug).slice(0, 1),
    {
      id: 'display',
      title: 'Display público',
      description: 'Abra este link na TV ou monitor da unidade.',
      url: getDisplayBranchUrl(origins, branchPublicId),
    },
    {
      id: 'customer',
      title: 'Atendimento presencial',
      description: 'Use este link ou QR Code na recepção para o cliente escolher o serviço e entrar na fila.',
      url: getCustomerBranchUrl(origins, branchPublicId),
    },
    ...getOrganizationAccessLinks(origins, organizationSlug).slice(1),
  ];
}
