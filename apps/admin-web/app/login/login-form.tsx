'use client';
import { configuredUrl } from '../../lib/configured-url';


import { FormEvent, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';

export function LoginForm() {
  const router = useRouter(); const search = useSearchParams();
  const [error, setError] = useState(search.get('reason') === 'session_expired' ? 'Sua sessão expirou. Entre novamente.' : search.get('activated') === '1' ? 'Conta ativada. Entre com suas novas credenciais.' : '');
  const [loading, setLoading] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setLoading(true); setError(''); const data = new FormData(event.currentTarget);
    const response = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: data.get('email'), password: data.get('password') }) });
    const body = (await response.json()) as { message?: string; role?: string; needsOnboarding?: boolean };
    if (!response.ok) { setError(body.message ?? 'Falha ao entrar.'); setLoading(false); return; }
    if (body.role === 'Attendant') {
      const attendant = configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL, 'http://localhost:3003', true);
      if (!attendant) { setError('Endereço do painel do atendente não configurado.'); setLoading(false); return; }
      window.location.assign(`${attendant}/login`);
      return;
    }
    router.replace(body.needsOnboarding ? '/onboarding' : '/dashboard'); router.refresh();
  }
  return <form onSubmit={submit} className="login-form"><label>E-mail<input name="email" type="email" defaultValue={search.get('email') ?? ''} autoComplete="email" required autoFocus /></label><label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>{error && <p className="form-error" role="alert">{error}</p>}<button type="submit" disabled={loading}>{loading ? 'Entrando…' : 'Entrar'}</button></form>;
}
