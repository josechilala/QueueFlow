import type { AppointmentOrigin } from './server-appointments';

export const appointmentOriginLabel: Record<AppointmentOrigin, string> = {
  PublicPortal: 'Cliente pelo portal público',
  AdminPanel: 'Painel administrativo',
};

export function buildPublicAppointmentUrl(baseUrl: string, publicToken: string) {
  const origin = baseUrl.trim().replace(/\/$/, '');
  const token = publicToken.trim();
  if (!origin || !token) return null;
  return `${origin}/meu-agendamento/${encodeURIComponent(token)}`;
}
