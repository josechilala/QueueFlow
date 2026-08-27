'use client';

export default function BranchesError({ reset }: { reset: () => void }) {
  return <main className="centered"><section className="notice" role="alert"><h1>Não foi possível carregar as unidades</h1><p>Verifique a API e tente novamente.</p><button type="button" onClick={reset}>Tentar novamente</button></section></main>;
}
