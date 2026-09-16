'use client';

import { useState } from 'react';
import { accessLinkAnchorProps, copyAccessLink, type AccessLink } from '../lib/access-links';

export function AccessLinks({ title, links, customerQrSvg }: { title: string; links: AccessLink[]; customerQrSvg?: string }) {
  const [feedback, setFeedback] = useState<string | null>(null);
  const [qrVisible, setQrVisible] = useState(false);

  async function copy(link: AccessLink) {
    if (!link.url) return;
    try {
      await copyAccessLink(link.url, navigator.clipboard);
      setFeedback(link.id);
      window.setTimeout(() => setFeedback(current => current === link.id ? null : current), 2200);
    } catch {
      setFeedback('error');
    }
  }

  return <section className="access-links" aria-labelledby="access-links-title">
    <div className="section-heading"><div><h3 id="access-links-title">{title}</h3><p className="muted">Acessos públicos configurados para sua operação.</p></div></div>
    <div className="access-link-grid">
      {links.map(link => <article className="access-link-card" key={link.id} data-link-id={link.id}>
        <div><h4>{link.title}</h4><p className="muted">{link.description}</p></div>
        {link.url ? <>
          <code>{link.url}</code>
          <div className="access-link-actions">
            <a className="secondary-button" href={link.url} {...accessLinkAnchorProps}>Abrir</a>
            <button type="button" onClick={() => copy(link)}>{feedback === link.id ? 'Link copiado' : 'Copiar link'}</button>
            {link.id === 'customer' && customerQrSvg && <button className="secondary" type="button" aria-expanded={qrVisible} onClick={() => setQrVisible(value => !value)}>{qrVisible ? 'Ocultar QR Code' : 'Mostrar QR Code'}</button>}
          </div>
          {link.id === 'customer' && customerQrSvg && qrVisible && <div className="access-link-qr" aria-label={`QR Code para ${link.url}`} dangerouslySetInnerHTML={{ __html: customerQrSvg }} />}
        </> : <p className="access-link-unavailable">Link indisponível. Configure a URL pública deste aplicativo.</p>}
      </article>)}
    </div>
    <span className={feedback === 'error' ? 'copy-feedback copy-error' : 'copy-feedback'} aria-live="polite">{feedback === 'error' ? 'Não foi possível copiar o link.' : ''}</span>
  </section>;
}
