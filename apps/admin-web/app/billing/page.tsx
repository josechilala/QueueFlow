import { redirect } from 'next/navigation';
import { AdminShell } from '../../components/admin-shell';
import { authFailure, apiUrl } from '../../lib/auth';
import { getServerSession } from '../../lib/server-session';

type BillingPlan = {
  code: string;
  name: string;
  monthlyPrice: number;
  annualPrice: number;
  includedAttendants: number | null;
  currency: string;
  annualDiscountPercent: number;
};

const money = (value: number) => new Intl.NumberFormat('pt-BR', {
  style: 'currency', currency: 'BRL'
}).format(value);

export default async function BillingPage() {
  const session = await getServerSession();
  const failure = authFailure(session.status);
  if (failure === 'unauthorized') redirect('/api/auth/refresh?returnTo=/billing');
  if (failure === 'forbidden' || (session.user && session.user.role !== 'Owner')) {
    return <main className="centered"><section className="notice"><h1>Acesso não permitido</h1><p>Somente o proprietário pode gerenciar assinaturas.</p></section></main>;
  }
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');

  let plans: BillingPlan[] | null = null;
  try {
    const response = await fetch(`${apiUrl}/api/v1/billing/plans`, { cache: 'no-store' });
    if (response.ok) plans = await response.json() as BillingPlan[];
  } catch {
    // The catalog may be unavailable while the API is offline.
  }

  return <AdminShell user={session.user}>
    <div className="page-heading">
      <h1>Assinatura e pagamentos</h1>
      <p className="muted">Consulte os planos disponíveis para sua empresa.</p>
    </div>
    <section className="notice">
      <h3>Contratação em preparação</h3>
      <p>O checkout ainda não está disponível. Nenhuma cobrança será criada nesta tela.</p>
    </section>
    {!plans ? <section className="notice"><p>Não foi possível consultar os planos no momento. Tente novamente quando a API estiver disponível.</p></section> :
      <section className="metric-grid" aria-label="Planos disponíveis">
        {plans.map(plan => <article className="metric-card" key={plan.code}>
          <h3>{plan.name}</h3>
          <p>Mensal</p>
          <strong>{money(plan.monthlyPrice)}</strong>
          <p>Anual: {money(plan.annualPrice)} (desconto de {plan.annualDiscountPercent}%)</p>
          <p>{plan.includedAttendants === null ? 'Capacidade de atendentes expansível' : `${plan.includedAttendants} atendentes incluídos`}</p>
          <p className="muted">Contratação indisponível durante a integração com o Asaas Sandbox.</p>
        </article>)}
      </section>}
  </AdminShell>;
}
