import { configuredUrl } from './configured-url';
import { notFound } from 'next/navigation';
export type SchedulingService = { publicId: string; name: string; description: string | null; attendanceMode: 'AppointmentOnly' | 'Hybrid'; canSchedule: boolean };
export type SchedulingBranch = { publicId: string; name: string; address: string | null; services: SchedulingService[] };
export type SchedulingOrganization = { slug: string; name: string; branches: SchedulingBranch[] };
export async function getSchedulingCatalog(slug: string): Promise<SchedulingOrganization> {
  const api = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  const response = await fetch(`${api}/api/v1/public/organizations/${encodeURIComponent(slug)}/scheduling`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Falha ao consultar o agendamento da empresa.');
  return response.json();
}
