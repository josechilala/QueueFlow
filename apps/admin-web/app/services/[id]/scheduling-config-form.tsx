'use client';

import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../../lib/branches';
import type { Service } from '../../../lib/services';
import { serviceResponseMessage } from '../../../lib/services';
import type { ScheduleBlock, SchedulingSettings, ServiceSchedule } from '../../../lib/server-scheduling';

const days = ['Domingo', 'Segunda-feira', 'Terça-feira', 'Quarta-feira', 'Quinta-feira', 'Sexta-feira', 'Sábado'];

export function SchedulingConfigForm({ service, branch, settings, schedules, blocks }: { service: Service; branch: Branch; settings: SchedulingSettings; schedules: ServiceSchedule[]; blocks: ScheduleBlock[] }) {
  const router = useRouter();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  async function send(path: string, method: 'POST' | 'PUT' | 'DELETE', body?: object) {
    setPending(true); setError('');
    const response = await fetch(path, { method, headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined });
    if (response.status === 401) { window.location.assign('/login'); return false; }
    if (!response.ok) { setError(await serviceResponseMessage(response)); setPending(false); return false; }
    router.refresh(); setPending(false); return true;
  }
  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget);
    await send(`/api/services/${service.id}/scheduling-settings`, 'PUT', {
      attendanceMode: form.get('attendanceMode'), slotDurationMinutes: Number(form.get('slotDurationMinutes')), capacityPerSlot: Number(form.get('capacityPerSlot')),
      minimumAdvanceMinutes: Number(form.get('minimumAdvanceMinutes')), maximumAdvanceDays: Number(form.get('maximumAdvanceDays')), lateToleranceMinutes: Number(form.get('lateToleranceMinutes')),
      cancellationDeadlineMinutes: Number(form.get('cancellationDeadlineMinutes')), checkInAdvanceMinutes: Number(form.get('checkInAdvanceMinutes')),
      allowCustomerCancellation: form.has('allowCustomerCancellation'), allowCustomerReschedule: form.has('allowCustomerReschedule'), requireConfirmation: form.has('requireConfirmation'), isActive: form.has('isActive'),
    });
  }
  async function addSchedule(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const element = event.currentTarget; const form = new FormData(element); if (await send(`/api/services/${service.id}/schedules`, 'POST', { dayOfWeek: Number(form.get('dayOfWeek')), startTime: form.get('startTime'), endTime: form.get('endTime') })) element.reset(); }
  async function addBlock(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const element = event.currentTarget; const form = new FormData(element); if (await send('/api/schedule-blocks', 'POST', { branchId: branch.id, serviceId: service.id, startAt: new Date(String(form.get('startAt'))).toISOString(), endAt: new Date(String(form.get('endAt'))).toISOString(), reason: form.get('reason'), blockType: form.get('blockType') })) element.reset(); }
  return <div className="scheduling-grid">
    <form className="entity-form" onSubmit={saveSettings}><h3>Regras do agendamento</h3>
      <label>Modo de atendimento<select name="attendanceMode" defaultValue={settings.attendanceMode}><option value="QueueOnly">Somente fila</option><option value="AppointmentOnly">Somente agendamento</option><option value="Hybrid">Híbrido</option></select></label>
      <div className="form-columns"><NumberField name="slotDurationMinutes" label="Duração do slot (min)" value={settings.slotDurationMinutes} /><NumberField name="capacityPerSlot" label="Capacidade por slot" value={settings.capacityPerSlot} /><NumberField name="minimumAdvanceMinutes" label="Antecedência mínima (min)" value={settings.minimumAdvanceMinutes} /><NumberField name="maximumAdvanceDays" label="Antecedência máxima (dias)" value={settings.maximumAdvanceDays} /><NumberField name="lateToleranceMinutes" label="Tolerância de atraso (min)" value={settings.lateToleranceMinutes} /><NumberField name="cancellationDeadlineMinutes" label="Prazo de cancelamento (min)" value={settings.cancellationDeadlineMinutes} /><NumberField name="checkInAdvanceMinutes" label="Check-in antecipado (min)" value={settings.checkInAdvanceMinutes} /></div>
      <fieldset><legend>Políticas</legend><Check name="allowCustomerCancellation" label="Permitir cancelamento" checked={settings.allowCustomerCancellation} /><Check name="allowCustomerReschedule" label="Permitir reagendamento" checked={settings.allowCustomerReschedule} /><Check name="requireConfirmation" label="Exigir confirmação" checked={settings.requireConfirmation} /><Check name="isActive" label="Agenda ativa" checked={settings.isActive} /></fieldset>
      <button disabled={pending}>Salvar regras</button>
    </form>
    <section><form className="entity-form compact-form" onSubmit={addSchedule}><h3>Horários semanais</h3><label>Dia<select name="dayOfWeek" defaultValue="1">{days.map((day, index) => <option key={day} value={index}>{day}</option>)}</select></label><label>Início<input name="startTime" type="time" required /></label><label>Fim<input name="endTime" type="time" required /></label><button disabled={pending}>Adicionar horário</button></form>
      <div className="table-wrap"><table><thead><tr><th>Dia</th><th>Período</th><th /></tr></thead><tbody>{schedules.map(item => <tr key={item.id}><td>{days[item.dayOfWeek]}</td><td>{item.startTime.slice(0, 5)}–{item.endTime.slice(0, 5)}</td><td><button className="danger-link" disabled={pending} onClick={() => send(`/api/services/${service.id}/schedules/${item.id}`, 'DELETE')}>Excluir</button></td></tr>)}</tbody></table></div>
    </section>
    <section className="scheduling-wide"><form className="entity-form compact-form" onSubmit={addBlock}><h3>Bloqueios e exceções</h3><label>Início<input name="startAt" type="datetime-local" required /></label><label>Fim<input name="endAt" type="datetime-local" required /></label><label>Tipo<select name="blockType"><option value="Manual">Manual</option><option value="Maintenance">Manutenção</option><option value="Holiday">Feriado</option></select></label><label>Motivo<input name="reason" required maxLength={300} /></label><button disabled={pending}>Adicionar bloqueio</button></form>
      <div className="table-wrap"><table><thead><tr><th>Período</th><th>Motivo</th><th>Tipo</th><th /></tr></thead><tbody>{blocks.map(item => <tr key={item.id}><td>{new Date(item.startAt).toLocaleString('pt-BR')} – {new Date(item.endAt).toLocaleString('pt-BR')}</td><td>{item.reason}</td><td>{item.blockType}</td><td><button className="danger-link" disabled={pending} onClick={() => send(`/api/schedule-blocks/${item.id}`, 'DELETE')}>Excluir</button></td></tr>)}</tbody></table></div>
    </section>{error && <p className="form-error scheduling-wide" role="alert">{error}</p>}
  </div>;
}

function NumberField({ name, label, value }: { name: string; label: string; value: number }) { return <label>{label}<input name={name} type="number" min={0} required defaultValue={value} /></label>; }
function Check({ name, label, checked }: { name: string; label: string; checked: boolean }) { return <label className="check-option"><input name={name} type="checkbox" defaultChecked={checked} /> {label}</label>; }
