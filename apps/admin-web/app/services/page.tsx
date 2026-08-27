import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure } from '../../lib/auth';
import { getBranches } from '../../lib/server-branches';
import { getServices } from '../../lib/server-services';
import { getServerSession } from '../../lib/server-session';
import { ServiceCreateForm } from './service-create-form';

const modeLabel = { QueueOnly: 'Somente fila', AppointmentOnly: 'Somente agendamento', Hybrid: 'Híbrido' } as const;

export default async function ServicesPage() {
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/services');
  if (sessionFailure === 'forbidden') return <AccessDenied />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const [services, branches] = await Promise.all([getServices(), getBranches()]);
  const responseStatus = services.status !== 200 ? services.status : branches.status;
  const failure = authFailure(responseStatus);
  if (failure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/services');
  if (failure === 'forbidden') return <AccessDenied />;
  if (!services.data || !branches.data) throw new Error('Não foi possível carregar os serviços.');
  const activeBranches = branches.data.filter(branch => branch.isActive);
  const branchNames = new Map(branches.data.map(branch => [branch.id, branch.name]));
  return <AdminShell user={session.user}>
    <div className="section-heading"><div><h3>Serviços</h3><p className="muted">Tipos de atendimento e respectivas agendas.</p></div></div>
    {activeBranches.length > 0 ? <ServiceCreateForm branches={activeBranches} /> : <section className="notice empty-state"><h3>Cadastre uma unidade ativa primeiro</h3><p>Todo serviço precisa estar vinculado a uma unidade real.</p></section>}
    {services.data.length === 0 ? <section className="notice empty-state"><h3>Nenhum serviço cadastrado</h3><p>Use o formulário acima para cadastrar o primeiro serviço.</p></section> :
      <div className="table-wrap"><table><thead><tr><th>Nome</th><th>Unidade</th><th>Modo</th><th>Prefixo</th><th>Status</th><th>Ações</th></tr></thead><tbody>{services.data.map(service => <tr key={service.id}><td><strong>{service.name}</strong>{service.description && <div className="muted">{service.description}</div>}</td><td>{branchNames.get(service.branchId) ?? 'Unidade indisponível'}</td><td>{modeLabel[service.attendanceMode]}</td><td>{service.prefix}</td><td><span className={service.isActive ? 'status active' : 'status inactive'}>{service.isActive ? 'Ativo' : 'Inativo'}</span></td><td><Link href={`/services/${service.id}`}>Configurar agenda</Link></td></tr>)}</tbody></table></div>}
  </AdminShell>;
}

function AccessDenied() {
  return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>Sua conta não possui permissão para administrar serviços.</p></section></main>;
}
