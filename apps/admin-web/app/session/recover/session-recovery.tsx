'use client';

import { useEffect, useState } from 'react';
import { recoverSession } from '../../../lib/session-recovery';

export function SessionRecovery({ returnTo }: { returnTo: string }) {
  const [message, setMessage] = useState('Verificando sua sessão…');
  const [busy, setBusy] = useState(true);
  const [run, setRun] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    setBusy(true);
    setMessage('Verificando sua sessão…');
    void recoverSession(returnTo, {
      signal: abort.signal,
      onNavigate: path => window.location.replace(path),
    }).then(result => {
      if (abort.signal.aborted || result === 'navigated') return;
      setMessage(result === 'conflict'
        ? 'Sua sessão mudou em outra aba. Entre novamente para continuar com segurança.'
        : 'Não foi possível renovar sua sessão agora. Entre novamente para continuar.');
    }).catch(() => {
      if (!abort.signal.aborted) setMessage('Não foi possível validar sua sessão. Entre novamente.');
    }).finally(() => {
      if (!abort.signal.aborted) setBusy(false);
    });
    return () => abort.abort();
  }, [returnTo, run]);

  return <>
    <h1>Verificando sua sessão</h1>
    <p role="status" aria-live="polite">{message}</p>
    <button disabled={busy} onClick={() => setRun(value => value + 1)}>{busy ? 'Aguarde…' : 'Tentar novamente'}</button>
    <p><a href={`/login?returnTo=${encodeURIComponent(returnTo)}`}>Entrar novamente</a></p>
  </>;
}
