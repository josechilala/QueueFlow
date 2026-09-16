import { getOnboardingProgress } from '../../lib/server-onboarding';
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

  if (session.user.role === 'Owner' && !(await getOnboardingProgress()).completed) redirect('/onboarding');
  const dashboard = await getDashboardSummary();
  const dashboardFailure = authFailure(dashboard.status);
  if (dashboardFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/dashboard');
  if (dashboardFailure === 'forbidden') return <Forbidden message="Sua conta não possui permissão para consultar os indicadores administrativos." />;
  if (!dashboard.summary) throw new Error('Não foi possível carregar o resumo administrativo.');

  const summary = dashboard.summary;
  const canShareAccess = canViewAccessLinks(session.user.role);
  const accessLinks = getOrganizationAccessLinks(getConfiguredAccessOrigins(), summary.organizationSlug);
  const metrics = [
    { label: 'Filas ativas', value: summary.activeQueues.toLocaleString('pt-BR') },
    { label: 'Aguardando', value: summary.waiting.toLocaleString('pt-BR') },
    { label: 'Espera média', value: `${Math.round(summary.averageWaitMinutes)} min` },
    { label: 'Atendidos hoje', value: summary.completedToday.toLocaleString('pt-BR') },
  ];

  return <AdminShell user={session.user}>
    <RealtimeRefresh />
    {session.user.role === 'Owner' && <Link href="/onboarding">Configuração inicial da operação</Link>}
    {canShareAccess && <AccessLinks title="Links de acesso" links={accessLinks} />}
    <section className="metric-grid" aria-label="Resumo operacional">
      {metrics.map(metric => <article className="metric-card" key={metric.label}><p>{metric.label}</p><strong>{metric.value}</strong></article>)}
    </section>
    {hasOperationalData(summary)
      ? <article className="notice"><h3>Dados carregados</h3><p>Indicadores consultados diretamente na API. Última atualização: {new Date(summary.generatedAt).toLocaleString('pt-BR')}.</p></article>
      : <article className="notice empty-state"><h3>Ainda não há dados operacionais</h3><p>Os indicadores permanecerão zerados até existirem filas e atendimentos cadastrados.</p></article>}
    <div className="section-heading"><div><h3>Filas em andamento</h3><p className="muted">Situação operacional atual das filas abertas ou pausadas.</p></div></div>
    {summary.queuesInProgress.length === 0
      ? <article className="notice empty-state"><p>Nenhuma fila está em andamento.</p></article>
      : <div className="table-wrap"><table><thead><tr><th>Fila</th><th>Unidade</th><th>Serviço</th><th>Status</th><th>Aguardando</th><th>Estimativa</th><th></th></tr></thead><tbody>{summary.queuesInProgress.map(queue => <tr key={queue.id}><td>{queue.name}</td><td>{queue.branchName}</td><td>{queue.serviceName}</td><td><span className={`status queue-${queue.status.toLowerCase()}`}>{queue.status === 'Open' ? 'Aberta' : 'Pausada'}</span></td><td>{queue.waiting}</td><td>{queue.estimatedWaitMinutes} min</td><td><Link href={`/queues/${queue.id}`}>Visualizar</Link></td></tr>)}</tbody></table></div>}
  </AdminShell>;
}

function Forbidden({ message }: { message: string }) {
  return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>{message}</p><LogoutButton /></section></main>;
}
