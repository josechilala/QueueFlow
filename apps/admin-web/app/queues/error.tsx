'use client';
export default function QueuesError({ reset }: { reset: () => void }) { return <main className="centered"><section className="notice"><h1>Não foi possível carregar as filas</h1><button onClick={reset}>Tentar novamente</button></section></main>; }
