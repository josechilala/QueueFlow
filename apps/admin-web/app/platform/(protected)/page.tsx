import { getPlatformDashboard } from '../../../lib/server-platform';
import styles from './overview.module.css';
export default async function PlatformDashboard() {
  const result = await getPlatformDashboard(); const data = result.data;
  if (!data) return <p className="form-error">Não foi possível carregar os indicadores.</p>;
  const metrics = [
    { label: 'Organizações', value: data.totalOrganizations, description: 'Total de organizações cadastradas.', icon: 'M4 21V3h12v18M2 21h20M8 7h4M8 11h4M8 15h4M16 9h4v12' },
    { label: 'Ativas', value: data.activeOrganizations, description: 'Organizações ativas na plataforma.', icon: 'M9 12l2 2 4-4M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0' },
    { label: 'Em teste', value: data.trials, description: 'Organizações em período de teste.', icon: 'M9 3h6M10 3v6l-6 10a1 1 0 0 0 1 2h14a1 1 0 0 0 1-2L14 9V3M8 15h8' },
    { label: 'Assinaturas ativas', value: data.activeSubscriptions, description: 'Assinaturas ativas no momento.', icon: 'M3 5h18v14H3zM3 9h18M7 15h3M14 14l2 2 3-3' },
    { label: 'Convites pendentes', value: data.pendingInvitations, description: 'Convites enviados aguardando aceite.', icon: 'M12 19H3V5h18v6M3 5l9 7 9-7M22 17a4 4 0 1 1-8 0 4 4 0 0 1 8 0M18 15v2l1 1' },
    { label: 'Convites expirados', value: data.expiredInvitations, description: 'Convites que expiraram.', icon: 'M12 19H3V5h18v6M3 5l9 7 9-7M22 17a4 4 0 1 1-8 0 4 4 0 0 1 8 0M17 16l2 2M19 16l-2 2' },
  ];

  return <>
    <div className="page-heading"><h1>Visão geral</h1><p className="muted">Operação global do QueueFlow.</p></div>
    <section className={styles.grid} aria-label="Indicadores da plataforma">
      {metrics.map(metric => <article className={styles.card} key={metric.label}>
        <div className={styles.heading}>
          <h3 className={styles.title}>{metric.label}</h3>
          <span className={styles.icon} aria-hidden="true">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" focusable="false"><path d={metric.icon} /></svg>
          </span>
        </div>
        <strong className={styles.value}>{metric.value}</strong>
        <p className={styles.description}>{metric.description}</p>
      </article>)}
    </section>
  </>;
}
