import { navigation, site } from '../lib/site';

export function Brand() {
  return <a className="brand" href="/" aria-label="QueueFlow — página inicial"><span className="brand-mark" aria-hidden="true">Q<span /></span>Queue<span>Flow</span></a>;
}

export function Header() {
  return <header className="site-header"><div className="container header-inner">
    <Brand />
    <nav className="desktop-nav" aria-label="Navegação principal">{navigation.map(([label, id]) => <a key={id} href={`/#${id}`}>{label}</a>)}</nav>
    <div className="header-actions"><a className="login-link" href={site.adminUrl}>Entrar <span aria-hidden="true">↗</span></a><a className="button button-small" href="/#contato">Começar teste grátis</a></div>
    <details className="mobile-menu"><summary aria-label="Menu de navegação"><span aria-hidden="true">☰</span> Menu</summary><nav aria-label="Navegação mobile">{navigation.map(([label, id]) => <a key={id} href={`/#${id}`}>{label}</a>)}<a href={site.adminUrl}>Entrar</a><a href="/#contato">Começar teste grátis</a></nav></details>
  </div></header>;
}
