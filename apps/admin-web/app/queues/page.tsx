import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { getBranches } from '../../lib/server-branches';
import { getQueues } from '../../lib/server-queues';
import { getServices } from '../../lib/server-services';
import { getServerSession } from '../../lib/server-session';
import { queueStatusLabel } from '../../lib/queues';
import { QueueCreateForm } from './queue-create-form';

export default async function QueuesPage() {
  const session = await getServerSession();
  if (session.status === 401) redirect('/api/auth/refresh?returnTo=/queues');
  if (session.status === 403) return <AccessDenied />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const [queues, branches, services] = await Promise.all([getQueues(), getBranches(), getServices()]);
  const status = [queues.status, branches.status, services.status].find(value => value !== 200) ?? 200;
  if (status === 401) redirect('/api/auth/refresh?returnTo=/queues');
  if (status === 403) return <AccessDenied />;
  if (!queues.data || !branches.data || !services.data) throw new Error('Não foi possível carregar as filas.');
  const activeBranches = branches.data.filter(branch => branch.isActive);
  const activeServices = services.data.filter(service => service.isActive);
  const branchNames = new Map(branches.data.map(branch => [branch.id, branch.name]));
  const serviceNames = new Map(services.data.map(service => [service.id, service.name]));
  const canCreate = activeBranches.length > 0 && activeServices.length > 0;
  return <AdminShell user={session.user}><div className="section-heading"><div><h3>Filas</h3><p className="muted">Filas operacionais vinculadas a uma unidade e um serviço.</p></div></div>{canCreate ? <QueueCreateForm branches={activeBranches} services={activeServices} /> : <section className="notice empty-state"><h3>Prepare o catálogo primeiro</h3><p>É necessário ter uma unidade e um serviço ativos para criar uma fila.</p></section>}{queues.data.length === 0 ? <section className="notice empty-state"><h3>Nenhuma fila cadastrada</h3><p>Crie a primeira fila usando o formulário acima.</p></section> : <div className="table-wrap"><table><thead><tr><th>Nome</th><th>Unidade</th><th>Serviço</th><th>Status</th><th>Capacidade</th><th></th></tr></thead><tbody>{queues.data.map(queue => <tr key={queue.id}><td>{queue.name}</td><td>{branchNames.get(queue.branchId) ?? 'Indisponível'}</td><td>{serviceNames.get(queue.serviceId) ?? 'Indisponível'}</td><td><span className={`status queue-${queue.status.toLowerCase()}`}>{queueStatusLabel[queue.status]}</span></td><td>{queue.capacity ?? 'Sem limite'}</td><td><Link href={`/queues/${queue.id}`}>Visualizar</Link></td></tr>)}</tbody></table></div>}</AdminShell>;
}
function AccessDenied() { return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>Sua conta não possui permissão para administrar filas.</p></section></main>; }
