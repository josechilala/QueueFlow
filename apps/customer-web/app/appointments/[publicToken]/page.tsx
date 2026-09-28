import { appointmentPresentation, type AppointmentStatus } from '../../../lib/appointment-status';
import { configuredUrl } from '../../../lib/configured-url';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { RealtimeRefresh } from '../../realtime-refresh';
import { AppointmentActions } from './appointment-actions';

type Appointment = { publicToken: string; confirmationCode: string; status: AppointmentStatus; organizationName: string; branchPublicId: string; branchName: string; servicePublicId: string; serviceName: string; scheduledStart: string; scheduledEnd: string; timeZone: string; canCancel: boolean; canReschedule: boolean; queueTicketToken: string | null; queueTicketNumber: string | null };

export default async function AppointmentPage({ params }: { params: Promise<{ publicToken: string }> }) {
  const { publicToken } = await params; const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  const response = await fetch(`${apiUrl}/api/v1/public/appointments/${encodeURIComponent(publicToken)}`, { cache: 'no-store' });
  if (response.status === 404) notFound(); if (!response.ok) throw new Error('Não foi possível carregar o agendamento.');
  const appointment = await response.json() as Appointment;
  const date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'full', timeStyle: 'short', timeZone: appointment.timeZone }).format(new Date(appointment.scheduledStart));
  return <main><RealtimeRefresh ticketToken={appointment.publicToken} /><div className="brand">QueueFlow</div><article><small>{appointmentPresentation[appointment.status].title}</small><h1>{appointment.serviceName}</h1><p className="queue-context">{appointment.organizationName} · {appointment.branchName}</p><div className="appointment-status">{appointmentPresentation[appointment.status].label}</div><p className="appointment-date">{date}</p>{appointment.queueTicketToken ? <div className="confirmation-code"><span>Sua senha na fila</span><strong>{appointment.queueTicketNumber}</strong><Link href={`/ticket/${appointment.queueTicketToken}`}>Acompanhar chamada</Link></div> : <div className="confirmation-code"><span>Código de confirmação</span><strong>{appointment.confirmationCode}</strong></div>}<AppointmentActions token={appointment.publicToken} branchPublicId={appointment.branchPublicId} servicePublicId={appointment.servicePublicId} canCancel={appointment.canCancel} canReschedule={appointment.canReschedule} /><p className="muted">Chegue com 10 minutos de antecedência. Guarde este endereço para acompanhar ou alterar seu agendamento.</p><Link href="/">Voltar ao início</Link></article></main>;
}
