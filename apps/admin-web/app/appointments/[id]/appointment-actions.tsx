'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import type { AppointmentStatus } from '../../../lib/server-appointments';

type ManagementAction = 'cancel' | 'no-show';

export function AppointmentActions({ id, status }: { id: string; status: AppointmentStatus }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const isActiveReservation = status === 'Scheduled' || status === 'Confirmed';

  async function act(action: ManagementAction) {
    const reason = window.prompt('Motivo (opcional):');
    if (reason === null) return;
    setPending(true);
    setError('');
    try {
      const response = await fetch(`/api/appointments/${id}/${action}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }),
      });
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setError(body?.detail ?? 'Não foi possível concluir a ação.');
        return;
      }
      router.refresh();
    } catch {
      setError('Não foi possível concluir a ação. Tente novamente.');
    } finally {
      setPending(false);
    }
  }

  return <div className="form-actions">
    {isActiveReservation && <button disabled={pending} className="danger-link" onClick={() => act('cancel')}>Cancelar</button>}
    {isActiveReservation && <button disabled={pending} className="secondary" onClick={() => act('no-show')}>Marcar no-show</button>}
    {error && <p className="form-error">{error}</p>}
  </div>;
}
