'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Counter } from '../../../lib/counters';
import { counterResponseMessage } from '../../../lib/counters';

export function CounterEditForm({ counter }: { counter: Counter }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  async function update(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError('');
    const form = new FormData(event.currentTarget);
    await finish(await fetch(`/api/counters/${counter.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: form.get('name') }) }), 'Ponto de atendimento atualizado.');
  }
  async function toggleStatus() {
    setPending(true); setError('');
    await finish(await fetch(`/api/counters/${counter.id}/status`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ isActive: !counter.isActive }) }), counter.isActive ? 'Ponto desativado.' : 'Ponto ativado.');
  }
  async function finish(response: Response, message: string) {
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await counterResponseMessage(response)); setPending(false); return; }
    router.refresh(); setPending(false); window.alert(message);
  }
  return <form className="entity-form" onSubmit={update}><h3>Dados do ponto</h3><label>Nome<input name="name" required maxLength={200} defaultValue={counter.name} /></label>{error && <p className="form-error" role="alert">{error}</p>}<div className="form-actions"><button disabled={pending}>{pending ? 'Salvando...' : 'Salvar alterações'}</button><button className="secondary" disabled={pending} type="button" onClick={toggleStatus}>{counter.isActive ? 'Desativar ponto' : 'Ativar ponto'}</button></div></form>;
}
