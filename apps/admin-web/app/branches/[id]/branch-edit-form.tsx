'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../../lib/branches';
import { responseMessage } from '../../../lib/branches';

export function BranchEditForm({ branch }: { branch: Branch }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);

  async function update(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPending(true);
    setError('');
    const form = new FormData(event.currentTarget);
    const response = await fetch(`/api/branches/${branch.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: form.get('name'), address: form.get('address') || null, timeZone: form.get('timeZone') }),
    });
    await finish(response, 'Unidade atualizada.');
  }

  async function toggleStatus() {
    setPending(true);
    setError('');
    const response = await fetch(`/api/branches/${branch.id}/status`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isActive: !branch.isActive }),
    });
    await finish(response, branch.isActive ? 'Unidade desativada.' : 'Unidade ativada.');
  }

  async function finish(response: Response, message: string) {
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await responseMessage(response)); setPending(false); return; }
    router.refresh();
    setPending(false);
    window.alert(message);
  }

  return (
    <form className="entity-form" onSubmit={update}>
      <h3>Dados da unidade</h3>
      <label>Nome<input name="name" required maxLength={200} defaultValue={branch.name} /></label>
      <label>Endereço<input name="address" maxLength={500} defaultValue={branch.address ?? ''} /></label>
      <label>Fuso horário<input name="timeZone" required maxLength={100} defaultValue={branch.timeZone} /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="form-actions"><button disabled={pending}>{pending ? 'Salvando...' : 'Salvar alterações'}</button><button className="secondary" disabled={pending} type="button" onClick={toggleStatus}>{branch.isActive ? 'Desativar unidade' : 'Ativar unidade'}</button></div>
    </form>
  );
}
