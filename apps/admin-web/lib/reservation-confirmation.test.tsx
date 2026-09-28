// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { AppointmentActions } from '../app/appointments/[id]/appointment-actions';
import { SchedulingConfigForm } from '../app/services/[id]/scheduling-config-form';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));
let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

it.each(['Scheduled', 'Confirmed'] as const)('does not offer approval or check-in for %s reservations', async status => {
  await act(async () => root.render(<AppointmentActions id="reservation" status={status} />));
  expect(container.textContent).not.toContain('Confirmar');
  expect(container.textContent).toContain('Cancelar');
  expect(container.textContent).toContain('Marcar no-show');
});

it('removes the approval setting and omits it from submissions even with legacy true data', async () => {
  const fetch = vi.fn().mockResolvedValue(Response.json({})); vi.stubGlobal('fetch', fetch);
  const service = { id: 'service', publicId: 'public', branchId: 'branch', name: 'Service', isActive: true, description: null, prefix: 'A', averageDurationMinutes: 30, attendanceMode: 'AppointmentOnly' as const };
  const branch = { id: 'branch', publicId: 'public', name: 'Branch', isActive: true, address: null, timeZone: 'UTC' };
  const settings = { serviceId: service.id, attendanceMode: service.attendanceMode, slotDurationMinutes: 30, capacityPerSlot: 1, minimumAdvanceMinutes: 60, maximumAdvanceDays: 30, lateToleranceMinutes: 10, cancellationDeadlineMinutes: 60, checkInAdvanceMinutes: 30, allowCustomerCancellation: true, allowCustomerReschedule: true, requireConfirmation: true, isActive: true };
  await act(async () => root.render(<SchedulingConfigForm service={service} branch={branch} settings={settings} schedules={[]} blocks={[]} />));
  expect(container.querySelector('[name="requireConfirmation"]')).toBeNull();
  expect(container.textContent).not.toContain('Exigir confirmação');
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  const payload = JSON.parse(fetch.mock.calls[0][1].body);
  expect(payload).not.toHaveProperty('requireConfirmation');
  expect(payload).toMatchObject({ capacityPerSlot: 1, checkInAdvanceMinutes: 30, isActive: true });
});
