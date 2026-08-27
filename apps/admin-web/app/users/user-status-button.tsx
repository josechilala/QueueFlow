'use client';
import { useState } from 'react'; import { useRouter } from 'next/navigation';
export function UserStatusButton({ id, active }: { id: string; active: boolean }) {
  const router = useRouter(); const [pending, setPending] = useState(false); const [error, setError] = useState('');
  async function change() { setPending(true); setError(''); const response = await fetch(`/api/users/${id}/status`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ isActive: !active }) }); if (!response.ok) { const body = await response.json().catch(() => null); setError(body?.detail ?? 'Falha ao alterar status.'); setPending(false); return; } router.refresh(); }
  return <div><button className="secondary" disabled={pending} onClick={change}>{pending ? 'Salvando...' : active ? 'Desativar' : 'Ativar'}</button>{error && <p className="form-error">{error}</p>}</div>;
}
