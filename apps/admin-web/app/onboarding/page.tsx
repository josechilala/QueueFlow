import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure } from '../../lib/auth';
import { getBranches } from '../../lib/server-branches';
import { getQueues } from '../../lib/server-queues';
import { getServerSession } from '../../lib/server-session';
import { getServices } from '../../lib/server-services';
import { getUsers } from '../../lib/server-users';
import { getOnboardingProgress } from '../../lib/server-onboarding';
import { CompleteOnboardingButton } from './complete-button';
import { AccessLinks } from '../../components/access-links';
import { getOrganizationAccessLinks } from '../../lib/access-links';
import { getConfiguredAccessOrigins } from '../../lib/server-access-links';
import { getDashboardSummary } from '../../lib/server-dashboard';

export default async function OnboardingPage() {
  const session = await getServerSession();
  if (authFailure(session.status) === 'unauthorized') redirect('/api/auth/refresh?returnTo=/onboarding');
  if (!session.user || session.user.role !== 'Owner') redirect('/dashboard');
  const [branches, services, queues, users] = await Promise.all([getBranches(), getServices(), getQueues(), getUsers()]);
  if ([branches, services, queues, users].some(result => result.status !== 200)) throw new Error('Não foi possível consultar o progresso da configuração.');
  const activeServices = (services.data ?? []).filter(service => service.isActive);
  const progress = await getOnboardingProgress();
  const operationReady = progress.operationReady;
  const branchReady = progress.branchReady;
  const dashboard = await getDashboardSummary();
  const links = dashboard.summary ? getOrganizationAccessLinks(getConfiguredAccessOrigins(), dashboard.summary.organizationSlug) : [];
  const steps = [
    ['Crie a primeira unidade', branchReady, '/branches'],
    ['Cadastre os serviços', activeServices.length > 0, '/services'],
    ['Configure fila e/ou agenda dos serviços', operationReady, activeServices[0] ? `/services/${activeServices[0].id}` : '/services'],
    ['Adicione a equipe', (users.data?.length ?? 0) > 1, '/users'],
    ['Links de acesso', branchReady, '#access-links'],
  ];
  return <AdminShell user={session.user}><div className="page-heading"><h1>Configure sua operação</h1><p className="muted">O progresso acompanha os cadastros existentes. Você pode começar atendendo como Owner e adicionar a equipe depois.</p></div><p>Próxima etapa: {({ branch: "Unidade", services: "Serviços", operation: "Fila/Agenda", links: "Equipe e links", dashboard: "Concluído" } as Record<string, string>)[progress.nextStep]}</p><section className="stack"><article className="notice"><h3>✓ Empresa</h3><p>Dados cadastrados na ativação.</p></article>{steps.map(([title, done, href]) => <article className="notice" key={String(title)}><h3>{done ? '✓ ' : ''}{title}</h3><p className="muted">{done ? 'Concluído.' : 'Ainda falta esta configuração.'}</p><Link href={String(href)}>{done ? 'Revisar' : 'Configurar'}</Link></article>)}</section><section id="access-links"><AccessLinks title="Links de acesso" links={links} /></section>{branchReady && operationReady ? <CompleteOnboardingButton /> : <p>Cadastre uma unidade, serviços e suas filas ou agendas para concluir.</p>}</AdminShell>;
}
