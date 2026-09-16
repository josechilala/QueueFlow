import { configuredUrl } from '../../../lib/configured-url';
import { notFound, redirect } from 'next/navigation';

type PublicService = { branchPublicId: string; publicId: string; canSchedule: boolean };

export default async function DirectSchedulePage({ params }: { params: Promise<{ branchPublicId: string }> }) {
  const { branchPublicId: servicePublicId } = await params;
  const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  const response = await fetch(`${apiUrl}/api/v1/public/services/${encodeURIComponent(servicePublicId)}`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Não foi possível carregar o serviço.');
  const service = await response.json() as PublicService;
  if (!service.canSchedule) notFound();
  redirect(`/agendar/${service.branchPublicId}/${service.publicId}`);
}
