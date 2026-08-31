import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { AdminShell } from '../../../components/admin-shell';
import { appointmentOriginLabel, buildPublicAppointmentUrl } from '../../../lib/appointment-trace';
import { authFailure } from '../../../lib/auth';
import { getAppointment } from '../../../lib/server-appointments';
import { getServerSession } from '../../../lib/server-session';
import { AppointmentActions } from './appointment-actions';
import { AppointmentPublicLink } from './appointment-public-link';

export default async function AppointmentPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  if (authFailure(session.status) === 'unauthorized') redirect(`/api/auth/refresh?returnTo=/appointments/${id}`);
  if (!session.user) notFound();
  const result = await getAppointment(id);
  if (result.status === 404) notFound();
  if (!result.data) throw new Error('Não foi possível carregar o agendamento.');
  const item = result.data;
  const customerOrigin = process.env.QUEUEFLOW_CUSTOMER_URL ?? (process.env.NODE_ENV === 'production' ? '' : 'http://localhost:3001');
  const publicUrl = buildPublicAppointmentUrl(customerOrigin, item.publicToken);
  const notificationLabel = item.notifications.generated === 0
    ? 'Nenhum lembrete gerado ainda'
    : `${item.notifications.sent} enviado(s), ${item.notifications.pending} pendente(s), ${item.notifications.failed} com falha`;

  return <AdminShell user={session.user}>
    <div className="section-heading"><div><Link href="/appointments">← Agendamentos</Link><h3>{item.customerName}</h3><p className="muted">{item.serviceName} · {item.branchName}</p></div></div>
    <section className="notice appointment-details">
      <h3>Rastreabilidade da reserva</h3>
      <dl>
        <div><dt>Data do atendimento</dt><dd>{new Date(item.scheduledStart).toLocaleString('pt-BR')}</dd></div>
        <div><dt>Status</dt><dd>{item.status}</dd></div>
        <div><dt>Criado em</dt><dd>{new Date(item.createdAt).toLocaleString('pt-BR')}</dd></div>
        <div><dt>Origem</dt><dd>{appointmentOriginLabel[item.origin]}</dd></div>
        <div><dt>Criado por</dt><dd>{item.createdBy}</dd></div>
        <div><dt>Código</dt><dd>{item.confirmationCode}</dd></div>
        <div><dt>Telefone</dt><dd>{item.customerPhone ?? '—'}</dd></div>
        <div><dt>E-mail</dt><dd>{item.customerEmail ?? '—'}</dd></div>
        <div><dt>Lembretes</dt><dd>{notificationLabel}</dd></div>
        {item.notifications.lastSentAt && <div><dt>Último envio</dt><dd>{new Date(item.notifications.lastSentAt).toLocaleString('pt-BR')}</dd></div>}
      </dl>
      <AppointmentActions id={item.id} status={item.status} />
    </section>
    <section className="notice appointment-link-card">
      <h3>Link individual do cliente</h3>
      <p className="muted">Use este endereço para ajudar o cliente a consultar, cancelar, reagendar ou fazer check-in quando permitido.</p>
      {publicUrl ? <AppointmentPublicLink url={publicUrl} /> : <p className="form-error">Link indisponível. Configure a URL do portal do cliente.</p>}
    </section>
    <div className="section-heading"><div><h3>Histórico de alterações</h3><p className="muted">Transições administrativas e operacionais registradas para esta reserva.</p></div></div>
    <div className="table-wrap"><table><thead><tr><th>Quando</th><th>Anterior</th><th>Novo</th><th>Motivo</th></tr></thead><tbody>{item.history.map((history, index) => <tr key={`${history.createdAt}-${index}`}><td>{new Date(history.createdAt).toLocaleString('pt-BR')}</td><td>{history.previousStatus}</td><td>{history.newStatus}</td><td>{history.reason ?? '—'}</td></tr>)}</tbody></table></div>
  </AdminShell>;
}
