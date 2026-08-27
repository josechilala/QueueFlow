'use client';

import { useRef, useState } from 'react';
import { useRouter } from 'next/navigation';

type Ticket = { ticketNumber: string; customerPublicToken: string; ticketsAhead: number; estimatedMinutes: number };

export function JoinQueueButton({ publicId }: { publicId: string }) {
  const router = useRouter();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const submitting = useRef(false);

  async function join() {
    if (submitting.current) return;
    submitting.current = true;
    setPending(true);
    setError('');
    const response = await fetch(`/api/queues/${encodeURIComponent(publicId)}/tickets`, { method: 'POST' });
    if (!response.ok) {
      setError('A fila não está disponível ou atingiu sua capacidade.');
      setPending(false);
      submitting.current = false;
      return;
    }
    const ticket = (await response.json()) as Ticket;
    router.push(`/ticket/${encodeURIComponent(ticket.customerPublicToken)}`);
  }

  return <>{error && <p className="join-error">{error}</p>}<button type="button" disabled={pending} onClick={join}>{pending ? 'Entrando...' : 'Entrar na fila'}</button></>;
}
