'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import { responseMessage } from '../../lib/branches';

export function BranchCreateForm() {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    setPending(true);
    setError('');
    const form = new FormData(formElement);
    const response = await fetch('/api/branches', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: form.get('name'), address: form.get('address') || null, timeZone: form.get('timeZone') }),
    });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await responseMessage(response)); setPending(false); return; }
    formElement.reset();
    router.refresh();
    setPending(false);
  }

  return (
    <form className="entity-form" onSubmit={submit}>
      <h3>Nova unidade</h3>
      <label>Nome<input name="name" required maxLength={200} /></label>
      <label>Endereço<input name="address" maxLength={500} /></label>
      <label>Fuso horário<input name="timeZone" required maxLength={100} defaultValue="America/Sao_Paulo" /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button disabled={pending}>{pending ? 'Salvando...' : 'Cadastrar unidade'}</button>
    </form>
  );
}
