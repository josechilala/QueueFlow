import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure } from '../../lib/auth';
import { getBranches } from '../../lib/server-branches';
import { getCounters } from '../../lib/server-counters';
import { getServerSession } from '../../lib/server-session';
import { CounterCreateForm } from './counter-create-form';

export default async function CountersPage() {
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/counters');
  if (sessionFailure === 'forbidden') return <AccessDenied />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const [counters, branches] = await Promise.all([getCounters(), getBranches()]);
  const status = counters.status !== 200 ? counters.status : branches.status;
  if (status === 401) redirect('/api/auth/refresh?returnTo=/counters');
  if (status === 403) return <AccessDenied />;
  if (!counters.data || !branches.data) throw new Error('Não foi possível carregar os pontos de atendimento.');
  const activeBranches = branches.data.filter(branch => branch.isActive);
  const branchNames = new Map(branches.data.map(branch => [branch.id, branch.name]));
  return <AdminShell user={session.user}><div className="section-heading"><div><h3>Guichês</h3><p className="muted">Guichês, mesas, salas ou posições onde os atendimentos acontecem.</p></div></div>{activeBranches.length ? <CounterCreateForm branches={activeBranches} /> : <section className="notice empty-state"><h3>Cadastre uma unidade ativa primeiro</h3><p>Todo ponto de atendimento precisa pertencer a uma unidade.</p></section>}{counters.data.length === 0 ? <section className="notice empty-state"><h3>Nenhum ponto cadastrado</h3><p>Cadastre o primeiro ponto de atendimento usando o formulário acima.</p></section> : <div className="table-wrap"><table><thead><tr><th>Nome</th><th>Unidade</th><th>Status</th><th></th></tr></thead><tbody>{counters.data.map(counter => <tr key={counter.id}><td>{counter.name}</td><td>{branchNames.get(counter.branchId) ?? 'Unidade indisponível'}</td><td><span className={counter.isActive ? 'status active' : 'status inactive'}>{counter.isActive ? 'Ativo' : 'Inativo'}</span></td><td><Link href={`/counters/${counter.id}`}>Editar</Link></td></tr>)}</tbody></table></div>}</AdminShell>;
}

function AccessDenied() { return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>Sua conta não possui permissão para administrar pontos de atendimento.</p></section></main>; }
