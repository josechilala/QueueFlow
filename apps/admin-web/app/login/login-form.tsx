'use client';
import { retryDelay, safeReturnTo } from '../../../../packages/session/recovery';
import { configuredUrl } from '../../lib/configured-url';
import { loginError } from '../../lib/login-feedback';


import { FormEvent, useEffect, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';

export function LoginForm() {
  const router = useRouter(); const search = useSearchParams();
  const [error, setError] = useState(search.get('reason') === 'session_expired' ? 'Sua sessão expirou. Entre novamente.' : search.get('activated') === '1' ? 'Conta ativada. Entre com suas novas credenciais.' : '');
  const [loading, setLoading] = useState(false);
  const active = useRef(false);
  const retryAt = useRef(0);
  const [remaining, setRemaining] = useState(0);
  const coolingDown = remaining > 0;
  useEffect(() => {
    if (!coolingDown) return;
    const timer = setInterval(() => setRemaining(Math.max(0, Math.ceil((retryAt.current - Date.now()) / 1000))), 1000);
    return () => clearInterval(timer);
  }, [coolingDown]);
  function pause(seconds: number) {
    retryAt.current = Date.now() + seconds * 1000;
    setRemaining(seconds);
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (active.current || Date.now() < retryAt.current) return;
    active.current = true; setLoading(true); setError(''); const data = new FormData(event.currentTarget);
    let navigating = false;
    try {
      const response = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: data.get('email'), password: data.get('password') }), redirect: 'error', signal: AbortSignal.timeout(90_000) });
      if (!response.ok) {
        setError(loginError(response.status));
        if (response.status === 429 || response.status >= 500) pause(retryDelay(response.headers.get('Retry-After'), response.status === 429 ? 60 : 5));
        return;
      }
      const body = (await response.json()) as { authenticated?: boolean; role?: string; needsOnboarding?: boolean };
      if (body?.authenticated !== true) throw new Error('Invalid login response');
      if (body.role === 'Attendant') {
        const attendant = configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL, 'http://localhost:3003', true);
        if (!attendant) { setError('Endereço do painel do atendente não configurado.'); setLoading(false); return; }
        window.location.assign(`${attendant}/login`);
        navigating = true;
        return;
      }
      router.replace(body.needsOnboarding ? '/onboarding' : safeReturnTo(search.get('returnTo'))); router.refresh();
      navigating = true;
    } catch { setError(loginError(502)); pause(5); }
    finally { if (!navigating) { active.current = false; setLoading(false); } }
  }
  return <form onSubmit={submit} className="login-form" aria-busy={loading}><label>E-mail<input name="email" type="email" defaultValue={search.get('email') ?? ''} autoComplete="email" required autoFocus /></label><label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>{error && <p className="form-error" role="alert">{error}</p>}{remaining > 0 && <p role="status">Tente novamente em {remaining} segundos.</p>}<button type="submit" disabled={loading || coolingDown}>{loading ? 'Entrando…' : 'Entrar'}</button></form>;
}
