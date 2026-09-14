import { notFound } from 'next/navigation';
import { AvailabilityView } from '../../../../../components/availability-view';
import { getSchedulingCatalog } from '../../../../../lib/public-scheduling';
export default async function SchedulingAvailability({ params, searchParams }: { params: Promise<{ slug: string; branchPublicId: string; servicePublicId: string }>; searchParams: Promise<{ date?: string; reschedule?: string }> }) {
  const ids = await params;
  const organization = await getSchedulingCatalog(ids.slug);
  const branch = organization.branches.find(value => value.publicId === ids.branchPublicId);
  const service = branch?.services.find(value => value.publicId === ids.servicePublicId && value.canSchedule);
  if (!branch || !service) notFound();
  return <AvailabilityView params={Promise.resolve(ids)} searchParams={searchParams} backHref={`/agendamento/${encodeURIComponent(organization.slug)}?unidade=${branch.publicId}`} />;
}
