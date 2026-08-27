import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure } from '../../lib/auth';
import { getBranches } from '../../lib/server-branches';
import { getServerSession } from '../../lib/server-session';
import { BranchCreateForm } from './branch-create-form';

export default async function BranchesPage() {
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/branches');
  if (sessionFailure === 'forbidden') return <AccessDenied />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');

  const result = await getBranches();
  const failure = authFailure(result.status);
  if (failure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/branches');
  if (failure === 'forbidden') return <AccessDenied />;
  if (!result.data) throw new Error('Não foi possível carregar as unidades.');

  return (
    <AdminShell user={session.user}>
      <div className="section-heading"><div><h3>Unidades</h3><p className="muted">Unidades físicas ou virtuais da sua organização.</p></div></div>
      <BranchCreateForm />
      {result.data.length === 0
        ? <section className="notice empty-state"><h3>Nenhuma unidade cadastrada</h3><p>Cadastre a primeira unidade usando o formulário acima.</p></section>
        : <div className="table-wrap"><table><thead><tr><th>Nome</th><th>Endereço</th><th>Fuso horário</th><th>Status</th><th></th></tr></thead><tbody>{result.data.map(branch => <tr key={branch.id}><td>{branch.name}</td><td>{branch.address ?? 'Não informado'}</td><td>{branch.timeZone}</td><td><span className={branch.isActive ? 'status active' : 'status inactive'}>{branch.isActive ? 'Ativa' : 'Inativa'}</span></td><td><Link href={`/branches/${branch.id}`}>Visualizar</Link></td></tr>)}</tbody></table></div>}
    </AdminShell>
  );
}

function AccessDenied() {
  return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>Sua conta não possui permissão para administrar unidades.</p></section></main>;
}
