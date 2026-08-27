import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { AdminShell } from '../../../components/admin-shell';
import { getCounter } from '../../../lib/server-counters';
import { getServerSession } from '../../../lib/server-session';
import { CounterEditForm } from './counter-edit-form';

export default async function CounterPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  if (session.status === 401) redirect(`/api/auth/refresh?returnTo=/counters/${id}`);
  if (session.status === 403) redirect('/counters');
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const result = await getCounter(id);
  if (result.status === 401) redirect(`/api/auth/refresh?returnTo=/counters/${id}`);
  if (result.status === 403) redirect('/counters');
  if (result.status === 404) notFound();
  if (!result.data) throw new Error('Não foi possível carregar o ponto de atendimento.');
  return <AdminShell user={session.user}><div className="section-heading"><div><Link href="/counters">← Voltar para guichês</Link><h3>{result.data.name}</h3><p className="muted">Status atual: {result.data.isActive ? 'Ativo' : 'Inativo'}</p></div></div><CounterEditForm counter={result.data} /></AdminShell>;
}
