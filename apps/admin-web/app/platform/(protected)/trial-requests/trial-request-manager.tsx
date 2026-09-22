'use client';
import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { PlatformTrialRequest } from '../../../../lib/server-platform';

const labels = { Pending: 'Pendente', Approved: 'Aprovada', Rejected: 'Recusada' };
export function TrialRequestManager({ initial }: { initial: PlatformTrialRequest[] }) {
  const [items, setItems] = useState(initial);
  const [busy, setBusy] = useState<string | null>(null);
  const lock = useRef(false);
  const [feedback, setFeedback] = useState<{ message: string; error: boolean } | null>(null);
  const router = useRouter();
  useEffect(() => setItems(initial), [initial]);

  async function decide(id: string, action: 'approve' | 'reject') {
    if (lock.current) return;
    lock.current = true; setBusy(id); setFeedback(null);
    try {
      const response = await fetch(`/api/platform/trial-requests/${encodeURIComponent(id)}/${action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}', signal: AbortSignal.timeout(45_000) });
      const body = await response.json().catch(() => null) as (PlatformTrialRequest & { message?: string; detail?: string }) | null;
      if (!response.ok || !body?.id) {
        setFeedback({ error: true, message: body?.detail ?? body?.message ?? 'Não foi possível concluir a decisão. Atualize a página e tente novamente.' });
        if (response.status === 409 || response.status === 401) router.refresh();
        return;
      }
      setItems(current => current.map(item => item.id === id ? body : item));
      setFeedback({ error: false, message: action === 'approve' ? 'Solicitação aprovada. Convite enviado por e-mail.' : 'Solicitação recusada.' });
    } catch { setFeedback({ error: true, message: 'Não foi possível confirmar o resultado. Atualize a página antes de tentar novamente.' }); }
    finally { lock.current = false; setBusy(null); }
  }

  return <div className="stack">
    {feedback && <p className={feedback.error ? 'form-error' : 'form-success'} role={feedback.error ? 'alert' : 'status'}>{feedback.message}</p>}
    {items.length === 0 ? <p className="notice">Nenhuma solicitação recebida.</p> : <div className="table-wrap"><table><caption>Até 500 solicitações, com pendentes primeiro.</caption><thead><tr><th>Nome</th><th>Empresa</th><th>E-mail</th><th>Telefone</th><th>Data</th><th>Status</th><th>Detalhes e ações</th></tr></thead><tbody>{items.map(item => <tr key={item.id}>
      <td>{item.name}</td><td>{item.companyName}</td><td>{item.email}</td><td>{item.phone}</td><td>{new Date(item.createdAt).toLocaleString('pt-BR')}</td><td>{labels[item.status]}</td>
      <td><details><summary>Ver detalhes de {item.companyName}</summary><p>Aceite dos Termos e Privacidade: {new Date(item.acceptedTermsAt).toLocaleString('pt-BR')} (versão {item.termsVersion}).</p><p>Decisão: {item.decidedAt ? new Date(item.decidedAt).toLocaleString('pt-BR') : 'Aguardando análise'}</p>{item.invitationId && <p>Convite: <code>{item.invitationId}</code>. <a href="/platform/invitations">Gerenciar convites</a></p>}
        {item.status === 'Pending' && <div className="form-actions"><button type="button" disabled={busy !== null} onClick={() => decide(item.id, 'approve')}>{busy === item.id ? 'Processando…' : 'Aprovar e enviar convite'}</button><button type="button" className="secondary" disabled={busy !== null} onClick={() => decide(item.id, 'reject')}>Recusar</button></div>}
      </details></td></tr>)}</tbody></table></div>}
  </div>;
}
