'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Queue, QueueStatus } from '../../../lib/queues';
import { queueResponseMessage } from '../../../lib/queues';

const actions: Partial<Record<QueueStatus, { action: 'open' | 'pause' | 'close'; label: string }[]>> = {
  Draft: [{ action: 'open', label: 'Abrir fila' }, { action: 'close', label: 'Fechar fila' }],
  Open: [{ action: 'pause', label: 'Pausar fila' }, { action: 'close', label: 'Fechar fila' }],
  Paused: [{ action: 'open', label: 'Reabrir fila' }, { action: 'close', label: 'Fechar fila' }],
};
export function QueueActions({ queue }: { queue: Queue }) {
  const router = useRouter(); const [pending, setPending] = useState(false); const [error, setError] = useState('');
  async function transition(action: string) {
    if (action === 'close' && !window.confirm('Fechar a fila é definitivo. Deseja continuar?')) return;
    setPending(true); setError('');
    const response = await fetch(`/api/queues/${queue.id}/${action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { setError(await queueResponseMessage(response)); setPending(false); return; }
    router.refresh(); setPending(false);
  }
  const available = actions[queue.status] ?? [];
  return <section className="entity-form"><h3>Operação da fila</h3>{error && <p className="form-error">{error}</p>}<div className="form-actions">{available.map(item => <button className={item.action === 'close' ? 'secondary' : undefined} disabled={pending} key={item.action} onClick={() => transition(item.action)}>{item.label}</button>)}{available.length === 0 && <p className="muted">Esta fila está encerrada e preservada para histórico.</p>}</div></section>;
}
