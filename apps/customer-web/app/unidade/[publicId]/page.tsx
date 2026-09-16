import { configuredUrl } from '../../../lib/configured-url';
import { RealtimeRefresh } from '../../realtime-refresh';
import Link from 'next/link';
import { notFound } from 'next/navigation';

type PublicQueue = { publicId: string; status: 'Draft' | 'Open' | 'Paused' | 'Closed'; waitingCount: number; estimatedWaitMinutes: number };
type PublicService = { publicId: string; name: string; description: string | null; attendanceMode: 'QueueOnly' | 'AppointmentOnly' | 'Hybrid'; queue: PublicQueue | null; canJoinQueue: boolean; canSchedule: boolean };
type PublicBranch = { publicId: string; organizationName: string; name: string; services: PublicService[] };

export default async function PublicBranchPage({ params }: { params: Promise<{ publicId: string }> }) {
  const { publicId } = await params;
  const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  const response = await fetch(`${apiUrl}/api/v1/public/branches/${encodeURIComponent(publicId)}`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Não foi possível carregar os serviços da unidade.');
  const branch = (await response.json()) as PublicBranch;
  return <main className="branch-page">
    <RealtimeRefresh queuePublicIds={branch.services.flatMap(service => service.queue ? [service.queue.publicId] : [])} />
    <div className="brand">QueueFlow</div>
    <header className="branch-heading"><small>{branch.organizationName}</small><h1>{branch.name}</h1><h2>Como podemos ajudar?</h2><p>Escolha um serviço</p></header>
    <section className="service-list">
      {branch.services.map(service => <article className="service-card" key={service.publicId}>
        <div><h2>{service.name}</h2>{service.description && <p className="muted">{service.description}</p>}<small>{service.attendanceMode === 'AppointmentOnly' ? 'Somente agendamento' : service.attendanceMode === 'Hybrid' ? 'Fila e agendamento' : 'Somente fila'}</small></div>
        {service.attendanceMode !== 'AppointmentOnly' && <div className="queue-summary">
          <span className={`availability ${service.canJoinQueue ? 'available' : 'unavailable'}`}>{service.canJoinQueue ? 'Fila disponível' : 'Fila indisponível'}</span>
          {service.queue && <><span><strong>{service.queue.waitingCount}</strong> aguardando</span><span>~{service.queue.estimatedWaitMinutes} min</span></>}
        </div>}
        {service.canJoinQueue && service.queue && <Link className="service-action" href={`/q/${service.queue.publicId}`}>Entrar na fila</Link>}
        {service.canSchedule && <Link className="service-action" href={`/agendar/${branch.publicId}/${service.publicId}`}>Agendar horário</Link>}
        {!service.canJoinQueue && !service.canSchedule && <span className="service-action disabled">Indisponível agora</span>}
      </article>)}
      {!branch.services.length && <article><h2>Nenhum serviço disponível</h2><p className="muted">Consulte a recepção da unidade.</p></article>}
    </section>
  </main>;
}
