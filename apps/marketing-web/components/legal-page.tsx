import type { ReactNode } from 'react';

export function LegalPage({ title, children }: { title: string; children: ReactNode }) {
  return <main id="conteudo" className="container legal-page"><a href="/">← Voltar para o início</a><p className="eyebrow" style={{ marginTop: 36 }}>QUEUEFLOW · INFORMAÇÕES LEGAIS</p><h1>{title}</h1><aside className="legal-notice" aria-label="Documento em revisão"><strong>Minuta — revisão jurídica obrigatória antes da publicação comercial.</strong><p>Este conteúdo organiza os assuntos que deverão compor o documento definitivo. Não representa termos finais nem uma política de privacidade aprovada. Identificação do responsável, canais de contato e condições específicas ainda precisam ser definidos e validados.</p></aside>{children}<p>Consulte também <a href="/termos/">Termos de uso</a> e <a href="/privacidade/">Privacidade</a>.</p></main>;
}
