import { Brand } from './header';
import { site } from '../lib/site';

export function Footer() {
  return <footer className="site-footer"><div className="container">
    <div className="footer-main"><div><Brand /><p>Menos filas. Mais tempo para atender bem.</p></div><nav aria-label="Navegação do rodapé"><a href="/#recursos">Recursos</a><a href="/#como-funciona">Como funciona</a><a href="/#planos">Planos</a><a href="/termos/">Termos</a><a href="/privacidade/">Privacidade</a><a href={site.adminUrl}>Entrar ↗</a></nav></div>
    <div className="footer-bottom"><span>© {new Date().getFullYear()} QueueFlow. Todos os direitos reservados.</span><span>Feito para organizar. Pensado para pessoas.</span></div>
  </div></footer>;
}
