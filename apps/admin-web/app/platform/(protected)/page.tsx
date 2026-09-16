import { getPlatformDashboard } from '../../../lib/server-platform';
export default async function PlatformDashboard() {
  const result = await getPlatformDashboard(); const data = result.data;
  if (!data) return <p className="form-error">Não foi possível carregar os indicadores.</p>;
  return <><div className="page-heading"><h1>Visão geral</h1><p className="muted">Operação global do QueueFlow.</p></div><section className="metric-grid">{[['Organizações', data.totalOrganizations], ['Ativas', data.activeOrganizations], ['Em teste', data.trials], ['Assinaturas ativas', data.activeSubscriptions], ['Convites pendentes', data.pendingInvitations], ['Convites expirados', data.expiredInvitations]].map(([label, value]) => <article className="metric-card" key={String(label)}><span>{label}</span><strong>{value}</strong></article>)}</section></>;
}
