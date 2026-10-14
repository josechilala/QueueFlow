'use client';

import { useEffect, useRef, useState } from 'react';
import styles from './billing-checkout.module.css';

export type BillingPlan = {
  code: string; name: string; monthlyPrice: number; annualPrice: number;
  includedAttendants: number | null; currency: string; annualDiscountPercent: number;
};
type Charge = { id: string; status: string; amount: number; currency: string; paymentUrl?: string;
  pixPayload?: string; pixEncodedImage?: string };
const money = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
const labels: Record<string, string> = { Pending: 'Aguardando pagamento', Paid: 'Pagamento confirmado',
  Uncertain: 'A confirmação do checkout está em análise. Não inicie outra cobrança.',
  Cancelled: 'Cobrança cancelada', Refunded: 'Pagamento devolvido' };

export function BillingCheckout({ plans, previewOnly = false }: { plans: BillingPlan[]; previewOnly?: boolean }) {
  const [planCode, setPlanCode] = useState(plans[0]?.code ?? 'professional');
  const [cycle, setCycle] = useState('Monthly');
  const [method, setMethod] = useState('Pix');
  const [enabled, setEnabled] = useState(false);
  const [availability, setAvailability] = useState<'loading' | 'ready' | 'disabled' | 'unauthorized' | 'forbidden' | 'unavailable'>('loading');
  const [starting, setStarting] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const [charge, setCharge] = useState<Charge | null>(null);
  const key = useRef<string | null>(null);
  const inFlight = useRef(false);
  const lookupInFlight = useRef(false);
  const blocksCheckout = !!charge && charge.status !== 'Cancelled';
  const plan = plans.find(value => value.code === planCode);
  useEffect(() => {
    if (previewOnly) return;
    const abort = new AbortController();
    fetch('/api/billing/capabilities', { signal: abort.signal, cache: 'no-store' })
      .then(async response => {
        if (abort.signal.aborted) return;
        if (!response.ok) {
          setAvailability(response.status === 401 ? 'unauthorized' : response.status === 403 ? 'forbidden' : 'unavailable');
          return;
        }
        const body = await response.json();
        if (abort.signal.aborted) return;
        if (typeof body.checkoutEnabled !== 'boolean') { setAvailability('unavailable'); return; }
        setEnabled(body.checkoutEnabled);
        setAvailability(body.checkoutEnabled ? 'ready' : 'disabled');
      })
      .catch(() => { if (!abort.signal.aborted) setAvailability('unavailable'); });
    return () => abort.abort();
  }, [previewOnly]);
  useEffect(() => {
    if (enabled && localStorage.getItem('queueflow.billing.charge')) void refresh();
  }, [enabled]);
  async function start(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (previewOnly || !enabled || inFlight.current || lookupInFlight.current || blocksCheckout) return;
    inFlight.current = true;
    setStarting(true); setError('');
    const form = new FormData(event.currentTarget);
    key.current ??= crypto.randomUUID();
    try {
      const response = await fetch('/api/billing/checkout', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ planCode, cycle, paymentMethod: method, idempotencyKey: key.current,
          customerName: form.get('customerName'), customerEmail: form.get('customerEmail'), cpfCnpj: form.get('cpfCnpj') }) });
      if (!response.ok) throw new Error(response.status === 409 ? 'Existe um checkout em andamento. Resolva a cobrança anterior antes de tentar novamente.' :
        'Não foi possível iniciar o checkout. Tente novamente com os mesmos dados.');
      const result = await response.json() as Charge;
      acceptCharge(result);
      localStorage.setItem('queueflow.billing.charge', result.id);
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Falha ao iniciar checkout.'); }
    finally { inFlight.current = false; setStarting(false); }
  }
  function acceptCharge(result: Charge) {
    setCharge(result);
    if (result.status === 'Cancelled') key.current = null;
  }
  async function refresh() {
    if (lookupInFlight.current || inFlight.current) return;
    const id = charge?.id ?? localStorage.getItem('queueflow.billing.charge');
    if (!id || !/^[0-9a-f-]{36}$/i.test(id)) { setError('Nenhuma cobrança salva neste navegador.'); return; }
    lookupInFlight.current = true;
    setRefreshing(true); setError('');
    try {
      const response = await fetch(`/api/billing/charges/${id}`, { cache: 'no-store' });
      if (!response.ok) throw new Error('Não foi possível consultar a cobrança nesta empresa.');
      acceptCharge(await response.json() as Charge);
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Falha ao consultar cobrança.'); }
    finally { lookupInFlight.current = false; setRefreshing(false); }
  }
  return <section className={`notice ${styles.card}`} aria-label="Contratação de plano">
    <div className={styles.heading}>
      <span className="eyebrow">Sua assinatura</span>
      <h2>Escolha seu plano</h2>
      <p className="muted">Selecione o plano, a periodicidade e a forma de pagamento para sua empresa.</p>
    </div>
    {previewOnly ? <p className={styles.message}>Consulte os valores e as formas de pagamento. A contratação é feita pelo proprietário no painel da empresa.</p> :
      availability === 'disabled' ? <p className={styles.message}>Contratação em preparação. O checkout permanecerá indisponível até a validação da integração.</p> :
      availability === 'unauthorized' ? <p className="form-error" role="alert">Não foi possível autenticar sua sessão para consultar pagamentos. <a href="/api/auth/refresh?returnTo=/billing">Renovar sessão</a></p> :
      availability === 'forbidden' ? <p className="form-error" role="alert">Sua conta não tem permissão para gerenciar pagamentos. Entre com a conta do proprietário da empresa.</p> :
      availability === 'unavailable' ? <p className="form-error" role="alert">Não foi possível consultar a disponibilidade dos pagamentos. Atualize a página para tentar novamente.</p> :
      availability === 'loading' && <p className={styles.message} role="status">Consultando disponibilidade dos pagamentos…</p>}
    <form className={styles.form} onSubmit={start} aria-busy={starting}>
      <fieldset className={styles.fieldset} disabled={starting || refreshing || blocksCheckout}>
        <legend className={styles.legend}>Plano e pagamento</legend>
        <div className={styles.grid}>
        <label>Plano <select value={planCode} onChange={event => setPlanCode(event.target.value)}>
          {plans.map(value => <option key={value.code} value={value.code}>{value.name}</option>)}
        </select></label>
        <label>Periodicidade <select value={cycle} onChange={event => setCycle(event.target.value)}>
          <option value="Monthly">Mensal</option><option value="Yearly">Anual — desconto de 5%</option>
        </select></label>
        <label>Pagamento <select value={method} onChange={event => setMethod(event.target.value)}>
          <option value="Pix">Pix</option><option value="CreditCard">Cartão de crédito</option>
        </select></label>
        </div>
        <div className={styles.summary} aria-label="Resumo financeiro">
          <span className="eyebrow">Total da contratação</span>
          {plan && <p className={styles.price}><strong>{money(cycle === 'Monthly' ? plan.monthlyPrice : plan.annualPrice)}</strong>
            <span>{cycle === 'Yearly' ? ' por ano, valor integral antecipado.' : ' por mês.'}</span></p>}
          <p className={styles.explanation}>{method === 'CreditCard' ? 'Informe o cartão somente na página de pagamento do Asaas. O QueueFlow não recebe número ou código de segurança.' :
          cycle === 'Monthly' ? 'Uma cobrança Pix será gerada a cada mês e precisará ser paga por você.' : 'O Pix anual é uma cobrança única. A renovação exige um novo pagamento.'}</p>
        </div>
        {!previewOnly && enabled && <>
          <h3 className={styles.subheading}>Dados do responsável</h3>
          <div className={styles.grid}>
          <label>Nome do responsável <input name="customerName" required maxLength={200} autoComplete="name" /></label>
          <label>E-mail <input name="customerEmail" type="email" required maxLength={320} autoComplete="email" /></label>
          <label>CPF ou CNPJ <input name="cpfCnpj" required pattern="([0-9]{11}|[0-9]{14})" maxLength={14} inputMode="numeric" aria-describedby="billing-document-hint" /><span id="billing-document-hint" className={styles.hint}>Somente números, sem pontos ou traços.</span></label>
          </div>
        </>}
        {!previewOnly && <div className={styles.actions}><button type="submit" disabled={!enabled || starting || refreshing || blocksCheckout}>{starting ? 'Processando…' : 'Iniciar pagamento'}</button></div>}
      </fieldset>
    </form>
    {error && <p className="form-error" role="alert">{error}</p>}
    {charge && <div className={styles.charge} aria-live="polite">
      <h3 className={styles.subheading}>Sua cobrança</h3>
      <p className={styles.chargeStatus}>{labels[charge.status] ?? 'Consultando pagamento'} — {money(charge.amount)}</p>
      {charge.paymentUrl && <a className="secondary-button" href={charge.paymentUrl} target="_blank" rel="noopener noreferrer">Abrir página de pagamento</a>}
      {charge.pixEncodedImage && <img src={`data:image/png;base64,${charge.pixEncodedImage}`} width={240} height={240} alt="QR Code para pagamento Pix" />}
      {charge.pixPayload && <><p>Abra seu banco e use o QR Code ou o Pix copia e cola.</p>
        <textarea readOnly aria-label="Pix copia e cola" value={charge.pixPayload} />
        <button className="secondary" type="button" onClick={() => { navigator.clipboard.writeText(charge.pixPayload!).catch(() => setError('Copie o código no campo acima.')); }}>Copiar Pix</button></>}
    </div>}
    {!previewOnly && enabled && <div className={styles.lookup}><button className="secondary" type="button" disabled={starting || refreshing} onClick={refresh}>Consultar cobrança</button>{refreshing && <span className="muted" role="status">Processando sua solicitação…</span>}</div>}
  </section>;
}
