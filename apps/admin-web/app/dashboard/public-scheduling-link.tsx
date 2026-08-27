'use client';

import { useState } from 'react';
import { copyPublicSchedulingUrl } from '../../lib/public-scheduling';

export function PublicSchedulingLink({ url }: { url: string }) {
  const [feedback, setFeedback] = useState<'idle' | 'copied' | 'error'>('idle');

  async function copy() {
    try {
      await copyPublicSchedulingUrl(url);
      setFeedback('copied');
      window.setTimeout(() => setFeedback('idle'), 2500);
    } catch {
      setFeedback('error');
    }
  }

  return <article className="public-scheduling-card">
    <div>
      <small>DIVULGAÇÃO</small>
      <h3>Agendamento público</h3>
      <p className="muted">Compartilhe este link para seus clientes agendarem pela internet.</p>
      <code>{url}</code>
    </div>
    <div className="public-scheduling-actions">
      <button type="button" onClick={copy}>{feedback === 'copied' ? '✓ Link copiado' : 'Copiar link'}</button>
      <a className="secondary-button" href={url} target="_blank" rel="noopener noreferrer">Abrir página</a>
    </div>
    <span className={feedback === 'error' ? 'copy-feedback copy-error' : 'copy-feedback'} aria-live="polite">{feedback === 'copied' ? 'O link foi copiado para a área de transferência.' : feedback === 'error' ? 'Não foi possível copiar. Selecione e copie o endereço exibido.' : ''}</span>
  </article>;
}
