'use client';

import { useEffect, useState } from 'react';
import { recoverSession } from '../../../lib/session-recovery';

export function SessionRecovery({ returnTo }: { returnTo: string }) {
  const [message, setMessage] = useState('Verificando sua sessão…');
  const [waiting, setWaiting] = useState(0);
  const [busy, setBusy] = useState(true);
  const [conflict, setConflict] = useState(false);
  const [run, setRun] = useState(0);
  useEffect(() => {
    const abort = new AbortController();
    // StrictMode may mount twice. The previous run releases the browser lock
    // before the replacement can reach the API.
    setBusy(true); setConflict(false);
    void recoverSession(returnTo, {
      signal: abort.signal,
      onWait: seconds => { if (!abort.signal.aborted) { setWaiting(seconds); setMessage('O serviço está se recuperando. Aguarde para tentar novamente.'); } },
      onNavigate: path => window.location.replace(path),
    }).then(result => {
      if (abort.signal.aborted || result === 'navigated') return;
      setConflict(result === 'conflict');
      setMessage(result === 'conflict'
        ? 'Não foi possível confirmar a renovação. Se outra aba já entrou, volte à página. Caso contrário, entre novamente.'
        : 'O serviço ainda está indisponível. Sua sessão foi preservada. Você pode tentar novamente.');
    }).catch(() => { if (!abort.signal.aborted) setMessage('Não foi possível conectar. Tente novamente em instantes.'); })
      .finally(() => { if (!abort.signal.aborted) { setBusy(false); setWaiting(0); } });
    return () => { abort.abort(); };
  }, [returnTo, run]);
  return <>
    <h1>Recuperando sua sessão</h1>
    <p role="status" aria-live="polite">{message}</p>
    {waiting > 0 && <p>Nova tentativa em {waiting} segundos.</p>}
    {!conflict && <button disabled={busy} onClick={() => setRun(value => value + 1)}>{busy ? 'Aguarde…' : 'Tentar novamente'}</button>}
    {conflict && <a href={returnTo}>Voltar à página</a>}
    <p><a href={`/login?returnTo=${encodeURIComponent(returnTo)}`}>Entrar novamente</a></p>
  </>;
}
