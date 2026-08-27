import { notFound } from 'next/navigation';
import { RealtimeRefresh } from '../../realtime-refresh';
import { JoinQueueButton } from './join-queue-button';
type PublicQueue = { publicId: string; name: string; organizationName: string; branchName: string; serviceName: string; status: 'Draft' | 'Open' | 'Paused' | 'Closed'; waitingCount: number; estimatedWaitMinutes: number; acceptsNewTickets: boolean };
export default async function PublicQueuePage({ params }: { params: Promise<{ publicId: string }> }) {
  const { publicId } = await params; const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260';
  const response = await fetch(`${apiUrl}/api/v1/public/queues/${encodeURIComponent(publicId)}`, { cache: 'no-store' });
  if (response.status === 404) notFound(); if (!response.ok) throw new Error('Não foi possível consultar a fila.');
  const queue = (await response.json()) as PublicQueue;
  return <main><RealtimeRefresh queuePublicId={queue.publicId} /><div className="brand">QueueFlow</div><article><small>{queue.organizationName}</small><h1 className="queue-title">{queue.name}</h1><p className="queue-context">{queue.branchName}</p><p className="muted">Serviço: {queue.serviceName}</p><div className={`availability ${queue.acceptsNewTickets ? 'available' : 'unavailable'}`}>{queue.acceptsNewTickets ? 'Fila aberta' : statusMessage(queue.status)}</div><section className="public-metrics"><div><strong>{queue.waitingCount}</strong><span>Aguardando</span></div><div><strong>{queue.estimatedWaitMinutes} min</strong><span>Espera estimada</span></div></section>{queue.acceptsNewTickets ? <JoinQueueButton publicId={queue.publicId} /> : <p>Esta fila não está aceitando novas entradas neste momento.</p>}</article></main>;
}
function statusMessage(status: PublicQueue['status']) { if (status === 'Paused') return 'Fila pausada'; if (status === 'Closed') return 'Fila encerrada'; return 'Fila ainda não aberta'; }
