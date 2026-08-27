import { redirect } from 'next/navigation';
import { getOperationContext, getSession } from '../../lib/server-data';
import { RealtimeRefresh } from './realtime-refresh';
import { Workstation } from './workstation';
export default async function WorkstationPage() {
  const [session, operation] = await Promise.all([getSession(), getOperationContext()]);
  if (session.status === 401 || operation.status === 401) redirect('/login');
  if (session.status === 403 || operation.status === 403) return <main className="login"><section className="card"><h1>Acesso não permitido</h1></section></main>;
  if (!session.data || !operation.data) throw new Error('Não foi possível carregar a operação.');
  return <main className="shell"><RealtimeRefresh /><header><div><div className="brand">QueueFlow</div><h1>Atendimento</h1><p>{session.data.name} · {session.data.role}</p></div><form action="/api/auth/logout" method="post"><button className="secondary">Sair</button></form></header><Workstation context={operation.data} /></main>;
}
