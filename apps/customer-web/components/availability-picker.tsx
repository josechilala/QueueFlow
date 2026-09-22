'use client';

import { useState } from 'react';
import { BookingForm } from '../app/agendar/[branchPublicId]/[servicePublicId]/booking-form';

export type Availability = { branchPublicId: string; servicePublicId: string; organizationName: string; branchName: string; serviceName: string; date: string; timeZone: string; minimumDate: string; maximumDate: string; availableDaysOfWeek: string[]; slots: { startAt: string; endAt: string; remainingCapacity: number }[] };

export function AvailabilityPicker({ data, queriedDate, rescheduleToken }: { data: Availability; queriedDate: string; rescheduleToken?: string }) {
  const [date, setDate] = useState(queriedDate);
  const [dirty, setDirty] = useState(false);
  const [consulting, setConsulting] = useState(false);
  const current = !dirty && !consulting && date === queriedDate && data.date === queriedDate;
  const dayNames: Record<string, string> = { Sunday: 'domingo', Monday: 'segunda', Tuesday: 'terça', Wednesday: 'quarta', Thursday: 'quinta', Friday: 'sexta', Saturday: 'sábado' };
  return <>
    <form className="date-filter" method="get" onSubmit={() => setConsulting(true)}>
      <input type="hidden" name="reschedule" value={rescheduleToken ?? ''} />
      <label>Escolha a data<input name="date" type="date" required min={data.minimumDate} max={data.maximumDate} value={date} onChange={event => { setDate(event.target.value); setDirty(true); }} /></label>
      <button disabled={consulting}>Consultar</button>
    </form>
    <p className="muted schedule-hint">Dias atendidos: {data.availableDaysOfWeek.map(day => dayNames[day] ?? day).join(', ') || 'nenhum dia configurado'}</p>
    {current ? <BookingForm key={`${data.branchPublicId}:${data.servicePublicId}:${queriedDate}`} organizationName={data.organizationName} branchName={data.branchName} serviceName={data.serviceName} branchPublicId={data.branchPublicId} servicePublicId={data.servicePublicId} slots={data.slots} timeZone={data.timeZone} queriedDate={queriedDate} rescheduleToken={rescheduleToken} />
      : <p role="status">{consulting ? 'Consultando horários…' : 'Consulte os horários da data selecionada antes de continuar.'}</p>}
  </>;
}
