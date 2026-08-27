'use client';
export default function CountersError({ reset }: { reset: () => void }) { return <main className="centered"><section className="notice"><h1>Não foi possível carregar os pontos de atendimento</h1><button onClick={reset}>Tentar novamente</button></section></main>; }
