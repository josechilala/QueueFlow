'use client';
import { safeReturnTo } from '../../../../packages/session/recovery';
import { configuredUrl } from '../../lib/configured-url';
import { loginError } from '../../lib/login-feedback';
import { correlationId } from '../../../../packages/session/correlation';

import { FormEvent, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';

export function LoginForm() {
  const router = useRouter();
  const search = useSearchParams();
  const [error, setError] = useState(search.get('reason') === 'session_expired'
    ? 'Sua sessão expirou. Entre novamente.'
    : search.get('activated') === '1' ? 'Conta ativada. Entre com suas novas credenciais.' : '');
  const [loading, setLoading] = useState(false);
  const active = useRef(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (active.current) return;
    active.current = true;
    setLoading(true);
    setError('');
    const data = new FormData(event.currentTarget);
    let navigating = false;
    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': correlationId() },
        body: JSON.stringify({ email: data.get('email'), password: data.get('password') }),
        redirect: 'error', signal: AbortSignal.timeout(20_000),
      });
      if (!response.ok) {
        setError(loginError(response.status));
        return;
      }
      const body = (await response.json()) as { authenticated?: boolean; role?: string };
      if (body?.authenticated !== true || typeof body.role !== 'string') throw new Error('Invalid login response');

      if (body.role === 'Attendant') {
        const attendant = configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL, 'http://localhost:3003', true);
        if (!attendant) { setError('Endereço do painel do atendente não configurado.'); return; }
        window.location.assign(`${attendant}/login`);
        navigating = true;
        return;
      }

      // Owner readiness is resolved after the HttpOnly session exists. This keeps
      // onboarding outside the credential request without allowing incomplete owners
      // to bypass the setup flow.
      const destination = body.role === 'Owner'
        ? `/post-login?returnTo=${encodeURIComponent(safeReturnTo(search.get('returnTo')))}`
        : safeReturnTo(search.get('returnTo'));
      router.replace(destination);
      navigating = true;
    } catch {
      setError(loginError(502));
    } finally {
      if (!navigating) { active.current = false; setLoading(false); }
    }
  }

  return <form onSubmit={submit} className="login-form" aria-busy={loading}>
    <label>E-mail<input name="email" type="email" defaultValue={search.get('email') ?? ''} autoComplete="email" required autoFocus /></label>
    <label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>
    {error && <p className="form-error" role="alert">{error}</p>}
    <button type="submit" disabled={loading}>{loading ? 'Entrando…' : 'Entrar'}</button>
  </form>;
}
