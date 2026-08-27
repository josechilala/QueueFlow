import Link from 'next/link';
import { notFound } from 'next/navigation';
import { BookingForm } from './booking-form';

type Slot = { startAt: string; endAt: string; remainingCapacity: number };
type Availability = { branchPublicId: string; servicePublicId: string; organizationName: string; branchName: string; serviceName: string; date: string; timeZone: string; minimumDate: string; maximumDate: string; availableDaysOfWeek: string[]; slots: Slot[] };

export default async function AvailabilityPage({ params, searchParams }: { params: Promise<{ branchPublicId: string; servicePublicId: string }>; searchParams: Promise<{ date?: string; reschedule?: string }> }) {
  const ids = await params; const query = await searchParams;
  const date = /^\d{4}-\d{2}-\d{2}$/.test(query.date ?? '') ? query.date! : new Date(Date.now() + 86400000).toISOString().slice(0, 10);
  const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260';
  const response = await fetch(`${apiUrl}/api/v1/public/branches/${encodeURIComponent(ids.branchPublicId)}/services/${encodeURIComponent(ids.servicePublicId)}/availability?date=${date}`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Não foi possível consultar os horários.');
  const data = await response.json() as Availability;
  const dayNames: Record<string, string> = { Sunday: 'domingo', Monday: 'segunda', Tuesday: 'terça', Wednesday: 'quarta', Thursday: 'quinta', Friday: 'sexta', Saturday: 'sábado' };
  return <main><div className="brand">QueueFlow</div><article><small>{query.reschedule ? 'REAGENDAMENTO' : 'AGENDAMENTO ONLINE'}</small><h1>{data.serviceName}</h1><p className="queue-context">{data.organizationName} · {data.branchName}</p><form className="date-filter"><input type="hidden" name="reschedule" value={query.reschedule ?? ''} /><label>Escolha a data<input name="date" type="date" min={data.minimumDate} max={data.maximumDate} defaultValue={date} /></label><button>Consultar</button></form><p className="muted schedule-hint">Dias atendidos: {data.availableDaysOfWeek.map(day => dayNames[day] ?? day).join(', ') || 'nenhum dia configurado'}</p><BookingForm organizationName={data.organizationName} branchName={data.branchName} serviceName={data.serviceName} branchPublicId={data.branchPublicId} servicePublicId={data.servicePublicId} slots={data.slots} timeZone={data.timeZone} rescheduleToken={query.reschedule} /><Link href={`/unidade/${data.branchPublicId}`}>← Voltar aos serviços</Link></article></main>;
}
