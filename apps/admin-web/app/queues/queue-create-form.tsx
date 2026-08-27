'use client';

import { FormEvent, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../lib/branches';
import { queueResponseMessage } from '../../lib/queues';
import type { Service } from '../../lib/services';

export function QueueCreateForm({ branches, services }: { branches: Branch[]; services: Service[] }) {
  const router = useRouter();
  const [branchId, setBranchId] = useState('');
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const availableServices = useMemo(() => services.filter(service => service.branchId === branchId && service.isActive), [branchId, services]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    setPending(true); setError('');
    const capacity = form.get('capacity');
    const response = await fetch('/api/queues', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ branchId: form.get('branchId'), serviceId: form.get('serviceId'), name: form.get('name'), capacity: capacity ? Number(capacity) : null }) });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await queueResponseMessage(response)); setPending(false); return; }
    formElement.reset(); setBranchId(''); router.refresh(); setPending(false);
  }
  return <form className="entity-form" onSubmit={submit}><h3>Nova fila</h3><label>Unidade<select name="branchId" required value={branchId} onChange={event => setBranchId(event.target.value)}><option value="" disabled>Selecione uma unidade</option>{branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select></label><label>Serviço<select name="serviceId" required defaultValue="" key={branchId}><option value="" disabled>{branchId ? 'Selecione um serviço' : 'Selecione a unidade primeiro'}</option>{availableServices.map(service => <option key={service.id} value={service.id}>{service.name}</option>)}</select></label><label>Nome<input name="name" required maxLength={200} placeholder="Ex.: Fila de consultas" /></label><label>Capacidade opcional<input name="capacity" type="number" min={1} placeholder="Sem limite" /></label>{error && <p className="form-error" role="alert">{error}</p>}<button disabled={pending || availableServices.length === 0}>{pending ? 'Salvando...' : 'Criar fila'}</button></form>;
}
