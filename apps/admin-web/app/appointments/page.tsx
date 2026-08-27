import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure } from '../../lib/auth';
import { getAppointments, type AppointmentStatus } from '../../lib/server-appointments';
import { getBranches } from '../../lib/server-branches';
import { getServices } from '../../lib/server-services';
import { getServerSession } from '../../lib/server-session';

const labels: Record<AppointmentStatus, string> = { Scheduled: 'Pendente', Confirmed: 'Confirmado', CheckedIn: 'Check-in', Completed: 'Concluído', Cancelled: 'Cancelado', NoShow: 'Não compareceu', Rescheduled: 'Reagendado' };
export default async function AppointmentsPage({ searchParams }: { searchParams: Promise<Record<string, string | undefined>> }) {
  const session = await getServerSession(); if (authFailure(session.status) === 'unauthorized') redirect('/api/auth/refresh?returnTo=/appointments'); if (!session.user) return <Denied />;
  const filters = await searchParams; const query = new URLSearchParams(); for (const key of ['from', 'to', 'status', 'branchId', 'serviceId']) if (filters[key]) query.set(key, filters[key]!);
  const [appointments, branches, services] = await Promise.all([getAppointments(query.toString()), getBranches(), getServices()]);
  const status = appointments.status !== 200 ? appointments.status : branches.status !== 200 ? branches.status : services.status; if (authFailure(status) === 'unauthorized') redirect('/api/auth/refresh?returnTo=/appointments'); if (!appointments.data || !branches.data || !services.data) throw new Error('Não foi possível carregar os agendamentos.');
  return <AdminShell user={session.user}><div className="section-heading"><div><h3>Agendamentos</h3><p className="muted">Consulte e opere as reservas da organização.</p></div></div><form className="report-filter"><label>De<input name="from" type="date" defaultValue={filters.from} /></label><label>Até<input name="to" type="date" defaultValue={filters.to} /></label><label>Status<select name="status" defaultValue={filters.status ?? ''}><option value="">Todos</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label>Unidade<select name="branchId" defaultValue={filters.branchId ?? ''}><option value="">Todas</option>{branches.data.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label>Serviço<select name="serviceId" defaultValue={filters.serviceId ?? ''}><option value="">Todos</option>{services.data.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><button>Filtrar</button></form>
  <div className="table-wrap"><table><thead><tr><th>Data</th><th>Cliente</th><th>Serviço</th><th>Unidade</th><th>Status</th><th /></tr></thead><tbody>{appointments.data.map(item => <tr key={item.id}><td>{new Date(item.scheduledStart).toLocaleString('pt-BR')}</td><td><strong>{item.customerName}</strong><div className="muted">{item.customerPhone ?? item.customerEmail}</div></td><td>{item.serviceName}</td><td>{item.branchName}</td><td><span className={`status appointment-${item.status.toLowerCase()}`}>{labels[item.status]}</span></td><td><Link href={`/appointments/${item.id}`}>Detalhes</Link></td></tr>)}</tbody></table></div></AdminShell>;
}
function Denied() { return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1></section></main>; }
