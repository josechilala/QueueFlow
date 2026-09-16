import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import QRCode from 'qrcode';
import { AccessLinks } from '../../../components/access-links';
import { AdminShell } from '../../../components/admin-shell';
import { canViewAccessLinks, getBranchAccessLinks } from '../../../lib/access-links';
import { authFailure } from '../../../lib/auth';
import { getBranch } from '../../../lib/server-branches';
import { getConfiguredAccessOrigins } from '../../../lib/server-access-links';
import { getDashboardSummary } from '../../../lib/server-dashboard';
import { getServerSession } from '../../../lib/server-session';
import { BranchEditForm } from './branch-edit-form';

export default async function BranchPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect(`/api/auth/refresh?returnTo=/branches/${id}`);
  if (sessionFailure === 'forbidden') redirect('/branches');
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');

  const [result, dashboard] = await Promise.all([getBranch(id), getDashboardSummary()]);
  if (result.status === 401) redirect(`/api/auth/refresh?returnTo=/branches/${id}`);
  if (dashboard.status === 401) redirect(`/api/auth/refresh?returnTo=/branches/${id}`);
  if (result.status === 403) redirect('/branches');
  if (dashboard.status === 403) redirect('/branches');
  if (result.status === 404) notFound();
  if (!result.data) throw new Error('Não foi possível carregar a unidade.');
  if (!dashboard.summary) throw new Error('Não foi possível carregar os links da organização.');

  const accessLinks = getBranchAccessLinks(getConfiguredAccessOrigins(), dashboard.summary.organizationSlug, result.data.publicId);
  const customerUrl = accessLinks.find(link => link.id === 'customer')?.url;
  const qrSvg = customerUrl ? await QRCode.toString(customerUrl, { type: 'svg', width: 260, margin: 2, errorCorrectionLevel: 'M' }) : undefined;

  return <AdminShell user={session.user}><div className="section-heading"><div><Link href="/branches">← Voltar para unidades</Link><h3>{result.data.name}</h3><p className="muted">Status atual: {result.data.isActive ? 'Ativa' : 'Inativa'}</p></div></div>{canViewAccessLinks(session.user.role) && <AccessLinks title="Links da unidade" links={accessLinks} customerQrSvg={qrSvg} />}<BranchEditForm branch={result.data} /></AdminShell>;
}
