'use client';

import { useState } from 'react';

export function PublicQueueLink({ url }: { url: string }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    await navigator.clipboard.writeText(url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2000);
  }
  return <div className="public-link"><a href={url} rel="noreferrer" target="_blank">{url}</a><button type="button" onClick={copy}>{copied ? 'Link copiado!' : 'Copiar link'}</button></div>;
}
