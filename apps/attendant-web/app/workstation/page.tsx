import { redirect } from 'next/navigation';
import { getOperationContext, getSession, getTodayAppointments } from '../../lib/server-data';
import { RealtimeRefresh } from './realtime-refresh';
import { Workstation } from './workstation';

export default async function WorkstationPage() {
  const [session, operation, appointments] = await Promise.all([
    getSession(),
    getOperationContext(),
    getTodayAppointments(),
  ]);
  if (session.status === 401 || operation.status === 401 || appointments.status === 401) redirect('/api/auth/refresh?returnTo=/workstation');
  if (session.status === 403 || operation.status === 403 || appointments.status === 403) {
    return <main className="login"><section className="card"><h1>Acesso não permitido</h1></section></main>;
  }
  if (!session.data || !operation.data || !appointments.data) throw new Error('Não foi possível carregar a operação.');

  return (
    <main className="shell">
      <RealtimeRefresh queuePublicIds={[...operation.data.queues.map(queue => queue.publicId), ...(operation.data.servicePublicIds ?? [])]} />
      <header>
        <div><div className="brand">QueueFlow</div><h1>Atendimento</h1><p>{session.data.name} · {session.data.role}</p></div>
        <form action="/api/auth/logout" method="post"><button className="secondary">Sair</button></form>
      </header>
      <Workstation context={operation.data} appointments={appointments.data} />
    </main>
  );
}
