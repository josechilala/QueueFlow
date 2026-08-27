'use client';
import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
export function LoginForm() {
  const router = useRouter(); const [error, setError] = useState(''); const [pending, setPending] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setPending(true); setError(''); const data = new FormData(event.currentTarget); const response = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: data.get('email'), password: data.get('password') }) }); if (!response.ok) { setError('E-mail ou senha inválidos.'); setPending(false); return; } router.replace('/workstation'); router.refresh(); }
  return <form onSubmit={submit}><label>E-mail<input name="email" type="email" required autoFocus /></label><label>Senha<input name="password" type="password" required /></label>{error && <p className="error">{error}</p>}<button disabled={pending}>{pending ? 'Entrando...' : 'Entrar'}</button></form>;
}
