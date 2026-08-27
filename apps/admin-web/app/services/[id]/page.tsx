import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { AdminShell } from '../../../components/admin-shell';
import { authFailure } from '../../../lib/auth';
import { getBranches } from '../../../lib/server-branches';
import { getSchedulingData } from '../../../lib/server-scheduling';
import { getServices } from '../../../lib/server-services';
import { getServerSession } from '../../../lib/server-session';
import { SchedulingConfigForm } from './scheduling-config-form';

export default async function ServiceSchedulingPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const session = await getServerSession();
  const sessionFailure = authFailure(session.status);
  if (sessionFailure === 'unauthorized') redirect(`/api/auth/refresh?returnTo=/services/${id}`);
  if (sessionFailure === 'forbidden') return <AccessDenied />;
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  const [services, branches, scheduling] = await Promise.all([getServices(), getBranches(), getSchedulingData(id)]);
  const status = services.status !== 200 ? services.status : branches.status !== 200 ? branches.status : scheduling.status;
  const failure = authFailure(status);
  if (failure === 'unauthorized') redirect(`/api/auth/refresh?returnTo=/services/${id}`);
  if (failure === 'forbidden') return <AccessDenied />;
  const service = services.data?.find(item => item.id === id);
  if (!service) notFound();
  if (!branches.data || !scheduling.settings || !scheduling.schedules || !scheduling.blocks) throw new Error('Não foi possível carregar a agenda.');
  const branch = branches.data.find(item => item.id === service.branchId);
  if (!branch) notFound();
  return <AdminShell user={session.user}>
    <div className="section-heading"><div><Link href="/services">← Serviços</Link><h3>Agenda de {service.name}</h3><p className="muted">{branch.name} · configure regras, horários e bloqueios.</p></div></div>
    <SchedulingConfigForm service={service} branch={branch} settings={scheduling.settings} schedules={scheduling.schedules} blocks={scheduling.blocks} />
  </AdminShell>;
}

function AccessDenied() { return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1></section></main>; }
