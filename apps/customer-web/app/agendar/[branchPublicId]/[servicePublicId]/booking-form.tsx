'use client';

import { FormEvent, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';

type Slot = { startAt: string; endAt: string; remainingCapacity: number };

export function BookingForm({ organizationName, branchName, serviceName, branchPublicId, servicePublicId, slots, timeZone, rescheduleToken }: { organizationName: string; branchName: string; serviceName: string; branchPublicId: string; servicePublicId: string; slots: Slot[]; timeZone: string; rescheduleToken?: string }) {
  const router = useRouter(); const [error, setError] = useState(''); const [pending, setPending] = useState(false); const [reviewing, setReviewing] = useState(false); const [selectedStart, setSelectedStart] = useState(slots[0]?.startAt ?? '');
  const timeFormatter = useMemo(() => new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone }), [timeZone]);
  const dateFormatter = useMemo(() => new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long', timeZone }), [timeZone]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); setPending(true); setError('');
    const response = await fetch(rescheduleToken ? `/api/appointments/${rescheduleToken}/reschedule` : '/api/appointments', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(rescheduleToken ? { scheduledStart: form.get('scheduledStart') } : { branchPublicId, servicePublicId, scheduledStart: form.get('scheduledStart'), customerName: form.get('customerName'), customerPhone: form.get('customerPhone') || null, customerEmail: form.get('customerEmail') || null }) });
    const body = await response.json().catch(() => null) as { publicToken?: string; detail?: string } | null;
    if (!response.ok || !body?.publicToken) { setError(response.status === 409 ? 'Este horário acabou de ficar indisponível. Escolha outro horário.' : body?.detail ?? 'Não foi possível reservar este horário. Atualize a disponibilidade e tente novamente.'); setPending(false); setReviewing(false); router.refresh(); return; }
    window.location.assign(`/meu-agendamento/${body.publicToken}`);
  }
  if (!slots.length) return <section className="empty-slots"><p>Não há horários disponíveis nesta data.</p><p className="muted">Escolha outra data no calendário acima.</p></section>;
  const selected = slots.find(slot => slot.startAt === selectedStart) ?? slots[0];
  return <form className="booking-form" onSubmit={submit}><input type="hidden" name="scheduledStart" value={selected.startAt} /><div className="slot-list">{slots.map(slot => <button type="button" className={`slot ${selected.startAt === slot.startAt ? 'selected' : ''}`} key={slot.startAt} onClick={() => { setSelectedStart(slot.startAt); setReviewing(false); }}><strong>{timeFormatter.format(new Date(slot.startAt))}</strong><span>{slot.remainingCapacity} vaga(s)</span></button>)}</div>{!rescheduleToken && <><label>Nome completo<input name="customerName" required maxLength={200} autoComplete="name" /></label><label>Telefone<input name="customerPhone" type="tel" maxLength={30} autoComplete="tel" /></label><label>E-mail<input name="customerEmail" type="email" maxLength={320} autoComplete="email" /></label></>}{reviewing && <section className="booking-review"><h2>Confirme seu agendamento</h2><dl><div><dt>Empresa</dt><dd>{organizationName}</dd></div><div><dt>Unidade</dt><dd>{branchName}</dd></div><div><dt>Serviço</dt><dd>{serviceName}</dd></div><div><dt>Data</dt><dd>{dateFormatter.format(new Date(selected.startAt))}</dd></div><div><dt>Horário</dt><dd>{timeFormatter.format(new Date(selected.startAt))}</dd></div></dl></section>}{error && <p className="join-error" role="alert">{error}</p>}{reviewing ? <div className="review-actions"><button type="button" className="secondary-button" disabled={pending} onClick={() => setReviewing(false)}>Voltar</button><button disabled={pending}>{pending ? 'Salvando...' : rescheduleToken ? 'Confirmar novo horário' : 'Confirmar agendamento'}</button></div> : <button type="button" onClick={() => setReviewing(true)}>Continuar</button>}</form>;
}
