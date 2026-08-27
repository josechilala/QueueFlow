import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import QRCode from 'qrcode';
import { AdminShell } from '../../../components/admin-shell';
import { authFailure } from '../../../lib/auth';
import { getBranch } from '../../../lib/server-branches';
import { getServerSession } from '../../../lib/server-session';
import { PublicQueueLink } from '../../queues/[id]/public-queue-link';
import { BranchEditForm } from './branch-edit-form';

export default async function BranchPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect(`/api/auth/refresh?returnTo=/branches/${id}`);
  if (sessionFailure === 'forbidden') redirect('/branches');
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');

  const result = await getBranch(id);
  if (result.status === 401) redirect(`/api/auth/refresh?returnTo=/branches/${id}`);
  if (result.status === 403) redirect('/branches');
  if (result.status === 404) notFound();
  if (!result.data) throw new Error('Não foi possível carregar a unidade.');

  const displayUrl = `${process.env.NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL ?? 'http://localhost:3002'}/display/${result.data.publicId}`;
  const customerUrl = `${(process.env.QUEUEFLOW_CUSTOMER_URL ?? 'http://localhost:3001').replace(/\/$/, '')}/unidade/${result.data.publicId}`;
  const qrSvg = await QRCode.toString(customerUrl, { type: 'svg', width: 260, margin: 2, errorCorrectionLevel: 'M' });

  return <AdminShell user={session.user}><div className="section-heading"><div><Link href="/branches">← Voltar para unidades</Link><h3>{result.data.name}</h3><p className="muted">Status atual: {result.data.isActive ? 'Ativa' : 'Inativa'}</p><a href={displayUrl} target="_blank" rel="noreferrer">Abrir painel público de TV</a></div></div><section className="qr-card"><div><h3>QR Code geral da unidade</h3><p className="muted">O cliente escolhe um serviço e entra na fila correspondente.</p><PublicQueueLink url={customerUrl} /></div><div className="qr-code" aria-label={`QR Code para ${customerUrl}`} dangerouslySetInnerHTML={{ __html: qrSvg }} /></section><BranchEditForm branch={result.data} /></AdminShell>;
}
