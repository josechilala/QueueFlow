import Link from 'next/link';
import { BillingCheckout, type BillingPlan } from '../../../../components/billing-checkout';
import { apiUrl } from '../../../../lib/auth';
import { sessionFetch } from '../../../../../../packages/session/server';

export default async function PlatformPaymentsPage() {
  const response = await sessionFetch(`${apiUrl}/api/v1/billing/plans`);
  const plans = response.ok ? await response.json() as BillingPlan[] : null;
  return <>
    <div className="page-heading">
      <h1>Planos e formas de pagamento</h1>
      <p className="muted">Consulte as opções de contratação disponíveis para as empresas.</p>
    </div>
    {plans?.length ? <BillingCheckout plans={plans} previewOnly /> :
      <section className="notice" role="alert"><p>Não foi possível consultar os planos. Tente novamente em alguns instantes.</p></section>}
    <section className="notice">
      <h2>Contratação pela empresa</h2>
      <p>Para iniciar um pagamento, entre com a conta do proprietário da empresa e acesse Assinatura e pagamentos.</p>
      <Link href="/billing">Abrir faturamento da empresa</Link>
    </section>
  </>;
}
