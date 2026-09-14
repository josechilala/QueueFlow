import Link from 'next/link';
import { notFound } from 'next/navigation';
import { getSchedulingCatalog } from '../../../lib/public-scheduling';
export default async function SchedulingPage({ params, searchParams }: { params: Promise<{ slug: string }>; searchParams: Promise<{ unidade?: string }> }) {
  const { slug } = await params; const { unidade } = await searchParams;
  const organization = await getSchedulingCatalog(slug);
  const base = `/agendamento/${encodeURIComponent(organization.slug)}`;
  const branch = unidade ? organization.branches.find(value => value.publicId === unidade) : organization.branches.length === 1 ? organization.branches[0] : undefined;
  if (unidade && !branch) notFound();
  return <main className="branch-page"><div className="brand">QueueFlow</div>
    <header className="branch-heading"><h1>{organization.name}</h1><h2>Agende seu horário</h2><p>{branch ? branch.name : 'Onde você deseja ser atendido?'}</p></header>
    <section className="service-list">
      {branch ? <><h2>Escolha um serviço</h2>{branch.services.map(service => <article className="service-card" key={service.publicId}><div><h2>{service.name}</h2>{service.description && <p className="muted">{service.description}</p>}</div>{service.canSchedule ? <Link className="service-action" href={`${base}/${branch.publicId}/${service.publicId}`}>Agendar horário</Link> : <span className="muted">Agendamento indisponível</span>}</article>)}{!branch.services.length && <p>Nenhum serviço de agendamento disponível nesta unidade.</p>}{organization.branches.length > 1 && <Link href={base}>Escolher outra unidade</Link>}</> : organization.branches.map(value => <article className="service-card" key={value.publicId}><div><h2>{value.name}</h2>{value.address && <p>{value.address}</p>}</div><Link className="service-action" href={`${base}?unidade=${value.publicId}`}>Selecionar unidade</Link></article>)}
      {!organization.branches.length && <p>Nenhuma unidade disponível para agendamento.</p>}
    </section>
  </main>;
}
