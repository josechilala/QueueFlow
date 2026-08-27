'use client';

import { useTransition } from 'react';
import { useRouter } from 'next/navigation';

export function RefreshTicketButton() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  return <button className="secondary-button" disabled={pending} onClick={() => startTransition(() => router.refresh())}>{pending ? 'Atualizando...' : 'Atualizar posição'}</button>;
}
