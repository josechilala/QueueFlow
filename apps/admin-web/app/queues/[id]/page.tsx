import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import QRCode from 'qrcode';
import { AdminShell } from '../../../components/admin-shell';
import { queueStatusLabel } from '../../../lib/queues';
import { getBranches } from '../../../lib/server-branches';
import { getQueue } from '../../../lib/server-queues';
import { getServices } from '../../../lib/server-services';
import { getServerSession } from '../../../lib/server-session';
import { QueueActions } from './queue-actions';
import { PublicQueueLink } from './public-queue-link';

export default async function QueuePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  if (session.status === 401) redirect(`/api/auth/refresh?returnTo=/queues/${id}`);
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const [queue, branches, services] = await Promise.all([getQueue(id), getBranches(), getServices()]);
  if (queue.status === 404) notFound();
  if (queue.status === 401) redirect(`/api/auth/refresh?returnTo=/queues/${id}`);
  if (!queue.data || !branches.data || !services.data) throw new Error('Não foi possível carregar a fila.');
  const branch = branches.data.find(item => item.id === queue.data!.branchId);
  const service = services.data.find(item => item.id === queue.data!.serviceId);
  const customerUrl = (process.env.QUEUEFLOW_CUSTOMER_URL ?? 'http://localhost:3001').replace(/\/$/, '');
  const publicUrl = `${customerUrl}/q/${queue.data.publicId}`;
  const qrSvg = await QRCode.toString(publicUrl, { type: 'svg', width: 260, margin: 2, errorCorrectionLevel: 'M' });
  return <AdminShell user={session.user}><div className="section-heading"><div><Link href="/queues">← Voltar para filas</Link><h3>{queue.data.name}</h3><p className="muted">{branch?.name} · {service?.name}</p></div></div><section className="metric-grid"><article className="metric-card"><p>Status</p><strong>{queueStatusLabel[queue.data.status]}</strong></article><article className="metric-card"><p>Capacidade</p><strong>{queue.data.capacity ?? '∞'}</strong></article><article className="metric-card"><p>Ativa</p><strong>{queue.data.isActive ? 'Sim' : 'Não'}</strong></article><article className="metric-card"><p>ID público</p><small>{queue.data.publicId}</small></article></section><section className="qr-card"><div><h3>Acesso público</h3><p className="muted">O cliente pode escanear o QR Code ou abrir o link abaixo.</p><PublicQueueLink url={publicUrl} /></div><div className="qr-code" aria-label={`QR Code para ${publicUrl}`} dangerouslySetInnerHTML={{ __html: qrSvg }} /></section><QueueActions queue={queue.data} /></AdminShell>;
}
