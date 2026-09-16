'use client';
import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
export function PlatformLoginForm() {
  const router = useRouter(); const [error, setError] = useState(''); const [loading, setLoading] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setLoading(true); setError(''); const form = new FormData(event.currentTarget); const response = await fetch('/api/platform/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: form.get('email'), password: form.get('password') }) }); if (!response.ok) { setError('E-mail ou senha inválidos.'); setLoading(false); return; } router.replace('/platform'); router.refresh(); }
  return <form className="login-form" onSubmit={submit}><label>E-mail<input name="email" type="email" required autoFocus /></label><label>Senha<input name="password" type="password" required /></label>{error && <p className="form-error">{error}</p>}<button disabled={loading}>{loading ? 'Entrando…' : 'Entrar'}</button></form>;
}
