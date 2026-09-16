'use client';

import { useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { OperationalAppointment, OperationalAppointmentStatus, OperationContext } from '../../lib/server-data';

const appointmentStatusLabel: Record<OperationalAppointmentStatus, string> = {
  AwaitingConfirmation: 'Aguardando confirmação',
  AwaitingArrival: 'Aguardando chegada',
  Waiting: 'Em espera',
  Called: 'Chamado',
  InService: 'Em atendimento',
  Completed: 'Concluído',
  NoShow: 'Não compareceu',
  Cancelled: 'Cancelado',
  Rescheduled: 'Reagendado',
};

export function Workstation({ context, appointments }: { context: OperationContext; appointments: OperationalAppointment[] }) {
  const router = useRouter();
  const busy = useRef(false);
  const [branchId, setBranchId] = useState(context.branches[0]?.id ?? '');
  const [queueId, setQueueId] = useState('');
  const [counterId, setCounterId] = useState('');
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const queues = useMemo(() => context.queues.filter(queue => queue.branchId === branchId), [context.queues, branchId]);
  const counters = useMemo(() => context.counters.filter(counter => counter.branchId === branchId), [context.counters, branchId]);
  const branchAppointments = useMemo(() => appointments.filter(appointment => appointment.branchId === branchId), [appointments, branchId]);
  const selectedQueue = queues.find(queue => queue.id === queueId);
  const ticket = context.currentTicket;

  async function action(url: string, body: object = {}) {
    if (busy.current) return;
    busy.current = true;
    setPending(true);
    setError('');
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (response.status === 401) {
      window.location.assign('/login');
      return;
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => null) as { detail?: string } | null;
      setError(problem?.detail ?? 'Não foi possível concluir a operação.');
      setPending(false);
      busy.current = false;
      return;
    }
    router.refresh();
    setPending(false);
    busy.current = false;
  }

  return (
    <>
      <section className="selectors">
        <label>Unidade<select value={branchId} onChange={event => { setBranchId(event.target.value); setQueueId(''); setCounterId(''); }}><option value="">Selecione</option>{context.branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select></label>
        <label>Fila<select value={queueId} onChange={event => setQueueId(event.target.value)}><option value="">Selecione</option>{queues.map(queue => <option key={queue.id} value={queue.id}>{queue.name} ({{ Open: 'aberta', Paused: 'pausada', Closed: 'fechada', Draft: 'rascunho' }[queue.status]})</option>)}</select></label>
        <label>Guichê<select value={counterId} onChange={event => setCounterId(event.target.value)}><option value="">Selecione</option>{counters.map(counter => <option key={counter.id} value={counter.id}>{counter.name}</option>)}</select></label>
      </section>

      <section className="metrics">
        <article><span>Aguardando</span><strong>{selectedQueue?.waitingCount ?? 0}</strong></article>
        <article><span>Ticket atual</span><strong>{ticket?.ticketNumber ?? '—'}</strong></article>
      </section>

      {error && <p className="error">{error}</p>}

      <section className="operation-card">
        {ticket ? <>
          <small>{ticket.status === 'Called' ? 'TICKET CHAMADO' : 'EM ATENDIMENTO'}</small>
          <h2>{ticket.ticketNumber}</h2>
          <p>{ticket.counterName}</p>
          <div className="actions">
            {ticket.status === 'Called' && <button disabled={pending} onClick={() => action(`/api/tickets/${ticket.id}/start`)}>Iniciar atendimento</button>}
            {ticket.status === 'InService' && <button disabled={pending} onClick={() => action(`/api/tickets/${ticket.id}/complete`)}>Concluir</button>}
            {ticket.status === 'Called' && <button className="secondary" disabled={pending} onClick={() => action(`/api/tickets/${ticket.id}/no-show`)}>Não compareceu</button>}
          </div>
        </> : <>
          <h2>Nenhum ticket em atendimento</h2>
          <p>Selecione uma fila aberta e um guichê para chamar o próximo.</p>
          <button disabled={pending || !queueId || !counterId || selectedQueue?.status !== 'Open'} onClick={() => action(`/api/queues/${queueId}/call-next`, { counterId })}>Chamar próximo</button>
        </>}
      </section>

      <section className="appointments-card">
        <div className="appointments-heading">
          <div><small>RECEPÇÃO</small><h2>Agendamentos de hoje</h2></div>
          <span>{branchAppointments.length} {branchAppointments.length === 1 ? 'cliente' : 'clientes'}</span>
        </div>
        {!branchId ? <p>Selecione uma unidade.</p> : branchAppointments.length === 0 ? (
          <p className="empty-state">Nenhum agendamento para hoje nesta unidade.</p>
        ) : (
          <div className="appointments-list">
            {branchAppointments.map(appointment => (
              <article className="appointment-row" key={appointment.id}>
                <time dateTime={appointment.scheduledStart}>{appointment.scheduledLocalTime}</time>
                <div className="appointment-customer">
                  <strong>{appointment.customerName}</strong>
                  <span>{appointment.serviceName}{appointment.ticketNumber ? ` · ${appointment.ticketNumber}` : ''}</span>
                  {appointment.arrivalConfirmed && <small>Chegada confirmada</small>}
                  {appointment.delayMinutes !== null && appointment.delayMinutes > 0 && <small className="appointment-delay">Atendimento atrasado em {appointment.delayMinutes} min</small>}
                </div>
                <span className={`appointment-status status-${appointment.operationalStatus.toLowerCase()}`}>{appointmentStatusLabel[appointment.operationalStatus]}</span>
                {appointment.operationalStatus === 'AwaitingArrival' && <button disabled={pending || !appointment.canConfirmArrival} onClick={() => action(`/api/appointments/${appointment.id}/check-in`)}>Confirmar chegada</button>}
              </article>
            ))}
          </div>
        )}
      </section>
    </>
  );
}
