'use client';

export default function DashboardError({ reset }: { reset: () => void }) {
  return (
    <main className="centered">
      <section className="notice" role="alert">
        <h1>Não foi possível carregar o painel</h1>
        <p>Verifique se a API está disponível e tente novamente.</p>
        <button type="button" onClick={reset}>Tentar novamente</button>
      </section>
    </main>
  );
}
