'use client';
export default function TicketError({ reset }: { reset: () => void }) { return <main><div className="brand">QueueFlow</div><article><h1 className="queue-title">Não foi possível consultar sua senha</h1><button onClick={reset}>Tentar novamente</button></article></main>; }
