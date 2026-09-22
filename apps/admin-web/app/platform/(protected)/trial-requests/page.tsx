import { redirect } from 'next/navigation';
import { getPlatformTrialRequests } from '../../../../lib/server-platform';
import { TrialRequestManager } from './trial-request-manager';

export default async function TrialRequestsPage() {
  const result = await getPlatformTrialRequests();
  if (result.status === 401) redirect('/api/platform/auth/refresh?returnTo=/platform/trial-requests');
  return <><div className="page-heading"><h1>Solicitações de teste</h1><p className="muted">Analise os pedidos antes de enviar o convite de ativação. O Trial começa somente após a ativação.</p></div>
    {result.data ? <TrialRequestManager initial={result.data} /> : <p className="form-error" role="alert">Não foi possível carregar as solicitações. Atualize a página para tentar novamente.</p>}</>;
}
