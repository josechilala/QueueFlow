'use client';

export default function PlatformError({ reset }: { reset: () => void }) {
  return <main className="centered"><section className="card"><h1>Serviço temporariamente indisponível</h1>
    <p>Não foi possível carregar o painel. Tente novamente em instantes.</p>
    <button onClick={reset}>Tentar novamente</button></section></main>;
}
