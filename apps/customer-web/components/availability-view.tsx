import { configuredUrl } from '../lib/configured-url';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { AvailabilityPicker, type Availability } from './availability-picker';
import { tomorrowInTimeZone } from '../lib/scheduling-date';

export async function AvailabilityView({ params, searchParams, backHref }: { params: Promise<{ branchPublicId: string; servicePublicId: string }>; searchParams: Promise<{ date?: string; reschedule?: string }>; backHref?: string }) {
  const ids = await params; const query = await searchParams;
  const now = new Date();
  const suppliedDate = /^\d{4}-\d{2}-\d{2}$/.test(query.date ?? '') ? query.date : undefined;
  const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  async function load(date: string): Promise<Availability> {
    const response = await fetch(`${apiUrl}/api/v1/public/branches/${encodeURIComponent(ids.branchPublicId)}/services/${encodeURIComponent(ids.servicePublicId)}/availability?date=${encodeURIComponent(date)}`, { cache: 'no-store' });
    if (response.status === 404) notFound();
    if (!response.ok) throw new Error('Não foi possível consultar os horários.');
    return response.json();
  }
  // Legacy public links have no branch timezone metadata. Probe once to obtain it;
  // never render these slots unless they belong to the final branch-local date.
  let date = suppliedDate ?? now.toISOString().slice(0, 10);
  let data = await load(date);
  if (!suppliedDate) {
    const initialDate = tomorrowInTimeZone(now, data.timeZone);
    if (initialDate !== date) data = await load(initialDate);
    date = initialDate;
  }
  return <main><div className="brand">QueueFlow</div><article><small>{query.reschedule ? 'REAGENDAMENTO' : 'AGENDAMENTO ONLINE'}</small><h1>{data.serviceName}</h1><p className="queue-context">{data.organizationName} · {data.branchName}</p><AvailabilityPicker key={`${data.branchPublicId}:${data.servicePublicId}:${date}:${query.reschedule ?? ""}`} data={data} queriedDate={date} rescheduleToken={query.reschedule} /><Link href={backHref ?? `/unidade/${data.branchPublicId}`}>← Voltar aos serviços</Link></article></main>;
}
