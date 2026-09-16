'use client';
import { useEffect, useRef, useState } from 'react';
import { ActivationFlow } from './[token]/activation-flow';

export default function ActivationPage() {
  const [token, setToken] = useState<string>();
  const captured = useRef(false);
  useEffect(() => {
    if (captured.current) return;
    captured.current = true;
    setToken(window.location.hash.slice(1));
    window.history.replaceState(null, '', window.location.pathname);
  }, []);
  return <main>{token === undefined ? <p>Carregando convite…</p> : <ActivationFlow token={token} />}</main>;
}
