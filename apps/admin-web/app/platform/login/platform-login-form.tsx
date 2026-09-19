'use client';
import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import { submitPlatformLogin } from '../../../lib/platform-login';
export function PlatformLoginForm() {
  const router = useRouter(); const [error, setError] = useState(''); const [loading, setLoading] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setLoading(true); setError('');
    try {
      const form = new FormData(event.currentTarget);
      const message = await submitPlatformLogin(form.get('email'), form.get('password'));
      if (message) { setError(message); return; }
      router.replace('/platform'); router.refresh();
    } finally { setLoading(false); }
  }
  return <form className="login-form" onSubmit={submit}><label>E-mail<input name="email" type="email" required autoFocus /></label><label>Senha<input name="password" type="password" required /></label>{error && <p className="form-error">{error}</p>}<button disabled={loading}>{loading ? 'Entrando…' : 'Entrar'}</button></form>;
}
