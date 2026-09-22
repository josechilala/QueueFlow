import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { AvailabilityPicker, type Availability } from './availability-picker';
import { AvailabilityView } from './availability-view';
import { localSlotDate, tomorrowInTimeZone } from '../lib/scheduling-date';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }), notFound: () => { throw new Error('not found'); } }));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
let container: HTMLDivElement;
let root: Root;
const slot = (date: string) => ({ startAt: `${date}T13:00:00+00:00`, endAt: `${date}T13:30:00+00:00`, remainingCapacity: 1 });
const availability = (date: string): Availability => ({ branchPublicId: 'branch', servicePublicId: 'service', organizationName: 'Empresa', branchName: 'Unidade', serviceName: 'Serviço', date, timeZone: 'America/Sao_Paulo', minimumDate: '2026-09-01', maximumDate: '2026-12-31', availableDaysOfWeek: ['Tuesday', 'Wednesday'], slots: [slot(date)] });
beforeEach(() => { container = document.createElement('div'); document.body.append(container); root = createRoot(container); });
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.useRealTimers(); });
const button = (text: string) => Array.from(container.querySelectorAll('button')).find(item => item.textContent === text)!;
async function changeDate(date: string) {
  const input = container.querySelector<HTMLInputElement>('input[name="date"]')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, date);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
}
async function mount(data: Availability, date = data.date) {
  await act(async () => root.render(<AvailabilityPicker key={date} data={data} queriedDate={date} />));
}
async function review() {
  await act(async () => (container.querySelector('.slot') as HTMLButtonElement).click());
  await act(async () => button('Continuar').click());
}

it('changing 23/09 to 22/09 immediately removes the old slot, selection and review, even when changing back', async () => {
  await mount(availability('2026-09-23')); await review();
  expect(container.querySelector('.booking-review')).not.toBeNull();
  await changeDate('2026-09-22');
  expect(container.querySelector('.slot')).toBeNull();
  expect(container.querySelector('.booking-review')).toBeNull();
  expect(container.querySelector('[name="scheduledStart"]')).toBeNull();
  expect(button('Continuar')).toBeUndefined();
  expect(button('Confirmar agendamento')).toBeUndefined();
  await changeDate('2026-09-23');
  expect(container.querySelector('.booking-form')).toBeNull();
});

it('selects 22/09, consults only that date and preserves 10:00 and the exact API instant in review and payload', async () => {
  await mount(availability('2026-09-23'));
  await changeDate('2026-09-22');
  const filter = container.querySelector<HTMLFormElement>('.date-filter')!;
  expect(filter.method).toBe('get');
  const requested = new FormData(filter).get('date') as string;
  expect(requested).toBe('2026-09-22');
  await act(async () => { filter.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
  expect(container.querySelector('.booking-form')).toBeNull();
  const fetchMock = vi.fn().mockResolvedValue(Response.json(availability(requested)));
  vi.stubGlobal('fetch', fetchMock);
  const page = await AvailabilityView({ params: Promise.resolve({ branchPublicId: 'branch', servicePublicId: 'service' }), searchParams: Promise.resolve({ date: requested }) });
  expect(fetchMock).toHaveBeenCalledTimes(1);
  expect(fetchMock.mock.calls[0][0]).toContain('/availability?date=2026-09-22');
  await act(async () => root.render(page));
  expect(button('Continuar').disabled).toBe(true);
  await review();
  const summary = container.querySelector('.booking-review')!.textContent;
  expect(summary).toContain('22 de setembro de 2026'); expect(summary).toContain('10:00');
  expect(summary).not.toContain('23 de setembro');
  // A rejected response keeps navigation out of this DOM test; capture the real request body.
  fetchMock.mockResolvedValue(Response.json({ detail: 'Test response' }, { status: 409 }));
  await act(async () => { container.querySelector('form.booking-form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
  const payload = JSON.parse(fetchMock.mock.calls[1][1].body);
  expect(payload.scheduledStart).toBe('2026-09-22T13:00:00+00:00');
  expect(localSlotDate(payload.scheduledStart, 'America/Sao_Paulo')).toBe('2026-09-22');
});

it('blocks a response date or slot that differs from the queried branch-local day', async () => {
  await mount(availability('2026-09-23'), '2026-09-22');
  expect(container.querySelector('.booking-form')).toBeNull();
  await mount({ ...availability('2026-09-22'), slots: [slot('2026-09-22'), slot('2026-09-23')] });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('não correspondem');
  expect(container.querySelector('.slot')).toBeNull();
  expect(button('Continuar')).toBeUndefined();
});

it('uses the branch civil day near UTC midnight, not tomorrow in UTC', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2026-09-22T01:00:00Z'));
  expect(tomorrowInTimeZone(new Date(), 'America/Sao_Paulo')).toBe('2026-09-22');
  const fetchMock = vi.fn().mockResolvedValue(Response.json(availability('2026-09-22')));
  vi.stubGlobal('fetch', fetchMock);
  const page = await AvailabilityView({ params: Promise.resolve({ branchPublicId: 'branch', servicePublicId: 'service' }), searchParams: Promise.resolve({}) });
  await act(async () => root.render(page));
  expect(container.querySelector<HTMLInputElement>('[name="date"]')!.value).toBe('2026-09-22');
  expect(fetchMock.mock.calls[0][0]).toContain('date=2026-09-22');
});

it('reloads availability for branch-local tomorrow after discovering the timezone', async () => {
  vi.useFakeTimers(); vi.setSystemTime(new Date('2026-09-22T15:00:00Z'));
  const fetchMock = vi.fn().mockResolvedValueOnce(Response.json(availability('2026-09-22'))).mockResolvedValueOnce(Response.json(availability('2026-09-23')));
  vi.stubGlobal('fetch', fetchMock);
  const page = await AvailabilityView({ params: Promise.resolve({ branchPublicId: 'branch', servicePublicId: 'service' }), searchParams: Promise.resolve({}) });
  await act(async () => root.render(page));
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(fetchMock.mock.calls[1][0]).toContain('date=2026-09-23');
  expect(container.querySelector<HTMLInputElement>('[name="date"]')!.value).toBe('2026-09-23');
});

it('compares instants in the branch timezone and handles calendar rollover without parsing civil dates', () => {
  expect(localSlotDate('2026-09-23T01:00:00Z', 'America/Sao_Paulo')).toBe('2026-09-22');
  expect(localSlotDate('2026-09-22', 'America/Sao_Paulo')).toBeNull();
  expect(localSlotDate('2026-09-22T10:00:00', 'America/Sao_Paulo')).toBeNull();
  expect(tomorrowInTimeZone(new Date('2026-12-31T15:00:00Z'), 'America/Sao_Paulo')).toBe('2027-01-01');
  expect(tomorrowInTimeZone(new Date('2028-02-28T15:00:00Z'), 'America/Sao_Paulo')).toBe('2028-02-29');
});
