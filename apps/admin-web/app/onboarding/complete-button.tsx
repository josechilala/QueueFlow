'use client';
import { useState } from 'react';
import { useRouter } from 'next/navigation';

export function CompleteOnboardingButton() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function complete() {
    setBusy(true);
    try {
      const response = await fetch('/api/onboarding/complete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
      if (!response.ok) { setError('Confira as configurações antes de concluir.'); return; }
      router.push('/dashboard'); router.refresh();
    } catch { setError('Não foi possível concluir. Tente novamente.'); }
    finally { setBusy(false); }
  }
  return <><button disabled={busy} onClick={complete}>Concluir e abrir Dashboard</button>{error && <p role="alert">{error}</p>}</>;
}
