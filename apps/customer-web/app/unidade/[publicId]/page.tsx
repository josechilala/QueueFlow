import { RealtimeRefresh } from '../../realtime-refresh';
import Link from 'next/link';
import { notFound } from 'next/navigation';

type QueueStatus = 'Draft' | 'Open' | 'Paused' | 'Closed';
type PublicBranchQueue = { publicId: string; name: string; serviceName: string; status: QueueStatus; waitingCount: number; estimatedWaitMinutes: number; acceptsNewTickets: boolean };
type PublicBranchService = { publicId: string; name: string; attendanceMode: 'AppointmentOnly' | 'Hybrid' };
type PublicBranch = { publicId: string; organizationName: string; name: string; queues: PublicBranchQueue[]; appointmentServices: PublicBranchService[] };
const statusLabels: Record<QueueStatus, string> = { Draft: 'Em preparação', Open: 'Aberta', Paused: 'Pausada', Closed: 'Fechada' };

export default async function PublicBranchPage({ params }: { params: Promise<{ publicId: string }> }) {
  const { publicId } = await params;
  const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260';
  const response = await fetch(`${apiUrl}/api/v1/public/branches/${encodeURIComponent(publicId)}`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Não foi possível carregar os serviços da unidade.');
  const branch = (await response.json()) as PublicBranch;
  return <main className="branch-page"><RealtimeRefresh queuePublicIds={branch.queues.map(queue => queue.publicId)} /><div className="brand">QueueFlow</div><header className="branch-heading"><small>{branch.organizationName}</small><h1>Como podemos ajudar?</h1><p>{branch.name}</p><span className="muted">Escolha entrar na fila ou reservar um horário.</span></header><section className="service-list">
    {branch.appointmentServices.map(service => <article className="service-card" key={`appointment-${service.publicId}`}><div><small>AGENDAMENTO</small><h2>{service.name}</h2><p className="muted">Consulte os próximos horários disponíveis.</p></div><Link className="service-action" href={`/agendar/${branch.publicId}/${service.publicId}`}>Agendar horário</Link></article>)}
    {branch.queues.map(queue => <article className="service-card" key={queue.publicId}><div><small>FILA</small><h2>{queue.serviceName}</h2>{queue.name !== queue.serviceName && <p className="muted">Fila: {queue.name}</p>}</div><div className="queue-summary"><span className={`availability ${queue.acceptsNewTickets ? 'available' : 'unavailable'}`}>{queue.acceptsNewTickets ? 'Disponível' : statusLabels[queue.status]}</span><span><strong>{queue.waitingCount}</strong> aguardando</span><span><strong>{queue.estimatedWaitMinutes} min</strong> estimados</span></div>{queue.acceptsNewTickets ? <Link className="service-action" href={`/q/${queue.publicId}`}>Entrar na fila</Link> : <span className="service-action disabled">Indisponível agora</span>}</article>)}
    {!branch.queues.length && !branch.appointmentServices.length && <article><h2>Nenhum serviço disponível</h2><p className="muted">Consulte a recepção da unidade.</p></article>}
  </section></main>;
}
