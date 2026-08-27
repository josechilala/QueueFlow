'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../lib/branches';
import { counterResponseMessage } from '../../lib/counters';

export function CounterCreateForm({ branches }: { branches: Branch[] }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    setPending(true); setError('');
    const response = await fetch('/api/counters', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ branchId: form.get('branchId'), name: form.get('name') }) });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await counterResponseMessage(response)); setPending(false); return; }
    formElement.reset(); router.refresh(); setPending(false);
  }
  return <form className="entity-form" onSubmit={submit}><h3>Novo ponto de atendimento</h3><label>Unidade<select name="branchId" required defaultValue=""><option value="" disabled>Selecione uma unidade</option>{branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select></label><label>Nome<input name="name" required maxLength={200} placeholder="Ex.: Guichê 01" /></label>{error && <p className="form-error" role="alert">{error}</p>}<button disabled={pending}>{pending ? 'Salvando...' : 'Cadastrar ponto'}</button></form>;
}
