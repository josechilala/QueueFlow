'use client';

import { useState } from 'react';

export function AppointmentPublicLink({ url }: { url: string }) {
  const [feedback, setFeedback] = useState<'idle' | 'copied' | 'error'>('idle');

  async function copy() {
    try {
      await navigator.clipboard.writeText(url);
      setFeedback('copied');
      window.setTimeout(() => setFeedback('idle'), 2500);
    } catch {
      setFeedback('error');
    }
  }

  return <div className="appointment-public-link">
    <code>{url}</code>
    <div className="form-actions">
      <button type="button" onClick={copy}>{feedback === 'copied' ? '✓ Link copiado' : 'Copiar link do cliente'}</button>
      <a className="secondary-button" href={url} target="_blank" rel="noopener noreferrer">Abrir acompanhamento</a>
    </div>
    <span className={feedback === 'error' ? 'copy-feedback copy-error' : 'copy-feedback'} aria-live="polite">{feedback === 'error' ? 'Não foi possível copiar o link.' : ''}</span>
  </div>;
}
