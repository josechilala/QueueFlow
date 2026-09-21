import { queueStatusLabel } from '../../lib/queues';
import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { AccessLinks } from '../../components/access-links';
import { RealtimeRefresh } from '../../components/realtime-refresh';
import { canViewAccessLinks, getOrganizationAccessLinks } from '../../lib/access-links';
import { authFailure } from '../../lib/auth';
import { hasOperationalData } from '../../lib/dashboard';
import { getDashboardSummary } from '../../lib/server-dashboard';
import { getServerSession } from '../../lib/server-session';
import { getConfiguredAccessOrigins } from '../../lib/server-access-links';
import { LogoutButton } from './logout-button';

export default async function DashboardPage() {
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/dashboard');
  if (sessionFailure === 'forbidden') return <Forbidden message="Sua conta não possui permissão para acessar o painel administrativo." />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');

  const dashboard = await getDashboardSummary();
  const dashboardFailure = authFailure(dashboard.status);
  if (dashboardFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/dashboard');
  if (dashboardFailure === 'forbidden') return <Forbidden message="Sua conta não possui permissão para consultar os indicadores administrativos." />;
  if (!dashboard.summary) throw new Error('Não foi possível carregar o resumo administrativo.');

  const summary = dashboard.summary;
  const canShareAccess = canViewAccessLinks(session.user.role);
  const accessLinks = getOrganizationAccessLinks(getConfiguredAccessOrigins(), summary.organizationSlug);
  const metrics = [
    { label: 'Filas abertas', value: summary.activeQueues },
    { label: 'Clientes aguardando', value: summary.waiting },
    { label: 'Clientes em atendimento', value: summary.inService },
    { label: 'Agendamentos de hoje', value: summary.appointmentsToday },
    { label: 'Próximos agendamentos', value: summary.upcomingAppointments },
    { label: 'Unidades ativas', value: summary.activeBranches },
  ];

  return <AdminShell user={session.user}>
    <RealtimeRefresh />
    <div className="page-heading"><h1>Visão geral</h1><p className="muted">Acompanhe as filas, os atendimentos e os agendamentos da sua empresa.</p></div>
    {canShareAccess && <AccessLinks title="Links de acesso" links={accessLinks} />}
    <section className="metric-grid" aria-label="Resumo operacional">
      {metrics.map(metric => <article className="metric-card" key={metric.label}><p>{metric.label}</p><strong>{metric.value.toLocaleString('pt-BR')}</strong></article>)}
    </section>
    <p className="muted">Agendamentos de hoje consideram o fuso de cada reserva, exceto cancelados e reagendados. Próximos agendamentos são reservas futuras pendentes ou confirmadas.</p>
    {hasOperationalData(summary)
      ? <article className="notice"><h3>Dados carregados</h3><p>Indicadores consultados diretamente na API. Última atualização: {new Date(summary.generatedAt).toLocaleString('pt-BR')}.</p></article>
      : <article className="notice empty-state"><h3>Ainda não há dados operacionais</h3><p>Os indicadores permanecerão zerados até existirem filas e atendimentos cadastrados.</p></article>}
    <div className="section-heading"><div><h3>Resumo das filas</h3><p className="muted">Unidade, serviço e situação atual de cada fila cadastrada.</p></div></div>
    {summary.queues.length === 0
      ? <article className="notice empty-state"><p>Nenhuma fila cadastrada.</p></article>
      : <div className="table-wrap"><table><thead><tr><th>Fila</th><th>Unidade</th><th>Serviço</th><th>Status</th><th>Aguardando</th><th></th></tr></thead><tbody>{summary.queues.map(queue => <tr key={queue.id}><td>{queue.name}</td><td>{queue.branchName}</td><td>{queue.serviceName}</td><td><span className={`status queue-${queue.status.toLowerCase()}`}>{queueStatusLabel[queue.status]}</span></td><td>{queue.waiting}</td><td><Link href={`/queues/${queue.id}`}>Visualizar</Link></td></tr>)}</tbody></table></div>}
  </AdminShell>;
}

function Forbidden({ message }: { message: string }) {
  return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>{message}</p><LogoutButton /></section></main>;
}
