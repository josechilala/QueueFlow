import Link from 'next/link';
import { notFound } from 'next/navigation';

type AttendanceMode = 'QueueOnly' | 'AppointmentOnly' | 'Hybrid';
type PublicService = { publicId: string; name: string; description: string | null; averageDurationMinutes: number; attendanceMode: AttendanceMode; queuePublicId: string | null; canJoinQueue: boolean; canSchedule: boolean };
type PublicBranch = { publicId: string; name: string; address: string | null; timeZone: string; services: PublicService[] };
type PublicOrganization = { slug: string; name: string; branches: PublicBranch[] };

const modeLabel: Record<AttendanceMode, string> = { QueueOnly: 'Atendimento por fila', AppointmentOnly: 'Atendimento com hora marcada', Hybrid: 'Fila ou hora marcada' };

export default async function PublicOrganizationPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260';
  const response = await fetch(`${apiUrl}/api/v1/public/organizations/${encodeURIComponent(slug)}`, { cache: 'no-store' });
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error('Não foi possível carregar a empresa.');
  const organization = await response.json() as PublicOrganization;

  return <main className="portal-page"><div className="brand">QueueFlow</div><header className="portal-hero"><small>AGENDAMENTO ONLINE</small><h1>{organization.name}</h1><p>Escolha uma unidade e reserve seu atendimento de onde estiver.</p></header>
    <section className="branch-list" aria-label="Unidades disponíveis">{organization.branches.map(branch => <article className="branch-card" key={branch.publicId}><header><small>UNIDADE</small><h2>{branch.name}</h2>{branch.address && <p className="muted">{branch.address}</p>}</header><div className="service-list">{branch.services.map(service => <section className="public-service" key={service.publicId}><div><span className="mode-badge">{modeLabel[service.attendanceMode]}</span><h3>{service.name}</h3>{service.description && <p>{service.description}</p>}<p className="muted">Duração estimada: {service.averageDurationMinutes} min</p></div><div className="public-actions">{service.canJoinQueue && service.queuePublicId && <Link className="secondary-button action-link" href={`/q/${service.queuePublicId}`}>Entrar na fila agora</Link>}{service.canSchedule && <Link className="service-action" href={`/agendar/${service.publicId}`}>Agendar atendimento</Link>}{!service.canJoinQueue && !service.canSchedule && <span className="service-action disabled">Indisponível</span>}</div></section>)}</div></article>)}{!organization.branches.length && <article><h2>Nenhuma unidade disponível</h2><p className="muted">Entre em contato com a empresa.</p></article>}</section>
  </main>;
}
