'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../lib/branches';
import { serviceResponseMessage } from '../../lib/services';

export function ServiceCreateForm({ branches }: { branches: Branch[] }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    setPending(true);
    setError('');
    const response = await fetch('/api/services', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ branchId: form.get('branchId'), name: form.get('name'), description: form.get('description') || null, prefix: form.get('prefix'), averageDurationMinutes: Number(form.get('averageDurationMinutes')) }),
    });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await serviceResponseMessage(response)); setPending(false); return; }
    formElement.reset();
    router.refresh();
    setPending(false);
  }

  return (
    <form className="entity-form" onSubmit={submit}>
      <h3>Novo serviço</h3>
      <label>Unidade<select name="branchId" required defaultValue=""><option value="" disabled>Selecione uma unidade</option>{branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select></label>
      <label>Nome<input name="name" required maxLength={200} /></label>
      <label>Descrição<textarea name="description" maxLength={1000} rows={3} /></label>
      <label>Prefixo da senha<input name="prefix" required maxLength={10} placeholder="Ex.: C" /></label>
      <label>Duração média estimada (minutos)<input name="averageDurationMinutes" type="number" required min={1} max={1440} defaultValue={15} /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button disabled={pending}>{pending ? 'Salvando...' : 'Cadastrar serviço'}</button>
    </form>
  );
}
