export default function DashboardLoading() {
  return (
    <main className="centered" aria-busy="true">
      <section className="notice">
        <h1>Carregando painel</h1>
        <p>Consultando os indicadores operacionais na API.</p>
      </section>
    </main>
  );
}
