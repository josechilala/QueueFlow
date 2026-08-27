'use client';
export default function QueueError({ reset }: { reset: () => void }) { return <main><div className="brand">QueueFlow</div><article><h1 className="queue-title">Não foi possível abrir a fila</h1><p className="muted">Verifique sua conexão e tente novamente.</p><button onClick={reset}>Tentar novamente</button></article></main>; }
