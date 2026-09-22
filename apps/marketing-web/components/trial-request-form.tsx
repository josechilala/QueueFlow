'use client';
import { useRef, useState, type FormEvent } from 'react';

export function trialRequestEndpoint(raw = process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL) {
  if (!raw?.trim()) return null;
  try {
    const url = new URL(raw.trim());
    const local = process.env.NODE_ENV !== 'production' && ['localhost', '127.0.0.1'].includes(url.hostname) && url.protocol === 'http:';
    if ((!local && url.protocol !== 'https:') || url.username || url.password || url.search || url.hash || url.pathname.replace(/\/+$/, '')) return null;
    return `${url.origin}/api/v1/public/trial-requests`;
  } catch { return null; }
}

export function TrialRequestForm() {
  const endpoint = trialRequestEndpoint();
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current || !endpoint) return;
    const form = event.currentTarget;
    if (!form.reportValidity()) return;
    const data = new FormData(form);
    lock.current = true; setBusy(true); setError('');
    try {
      const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'omit', signal: AbortSignal.timeout(20_000), body: JSON.stringify({ name: String(data.get('name') ?? '').trim(), email: String(data.get('email') ?? '').trim(), companyName: String(data.get('companyName') ?? '').trim(), phone: String(data.get('phone') ?? '').trim(), acceptedTerms: data.get('acceptedTerms') === 'on' }) });
      if (response.status === 202) { setSuccess(true); return; }
      const body = await response.json().catch(() => null) as { detail?: string } | null;
      setError(response.status === 429 ? 'Muitas tentativas. Aguarde alguns minutos antes de enviar novamente.' : response.status === 400 ? body?.detail ?? 'Confira os campos e tente novamente.' : 'Não foi possível enviar agora. Tente novamente em instantes.');
    } catch { setError('Não foi possível confirmar o envio. Verifique sua conexão e tente novamente.'); }
    finally { lock.current = false; setBusy(false); }
  }
  if (success) return <div className="trial-request-success" role="status"><h3>Solicitação recebida.</h3><p>Vamos analisar seus dados e você receberá as próximas instruções por e-mail.</p></div>;
  return <form className="trial-request-form" onSubmit={submit} aria-busy={busy} aria-label="Solicitação de teste grátis">
    <label>Nome<input name="name" autoComplete="name" minLength={2} maxLength={200} required disabled={busy} /></label>
    <label>E-mail profissional<input name="email" type="email" autoComplete="email" maxLength={320} required disabled={busy} /></label>
    <label>Nome da empresa<input name="companyName" autoComplete="organization" minLength={2} maxLength={200} required disabled={busy} /></label>
    <label>Telefone / WhatsApp<input name="phone" type="tel" autoComplete="tel" maxLength={30} required disabled={busy} /></label>
    <label className="trial-consent"><input name="acceptedTerms" type="checkbox" required disabled={busy} /><span>Aceito os <a href="/termos" target="_blank" rel="noopener">Termos de Uso</a> e a <a href="/privacidade" target="_blank" rel="noopener">Política de Privacidade</a>.</span></label>
    {error && <p className="trial-request-error" role="alert">{error}</p>}
    {!endpoint && <p className="trial-request-error" role="status">Solicitações temporariamente indisponíveis. Tente novamente mais tarde.</p>}
    <button type="submit" className="button button-light" disabled={busy || !endpoint}>{busy ? 'Enviando…' : 'Solicitar teste grátis'}</button>
  </form>;
}
