import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, expect, it, vi } from 'vitest';
import AppointmentPage from './page';

vi.mock('../../realtime-refresh', () => ({ RealtimeRefresh: () => null }));
vi.mock('./appointment-actions', () => ({ AppointmentActions: () => null }));
afterEach(() => vi.unstubAllGlobals());

it.each([
  ['Scheduled', 'AGENDAMENTO REGISTRADO'],
  ['Confirmed', 'AGENDAMENTO CONFIRMADO'],
  ['Cancelled', 'AGENDAMENTO CANCELADO'],
  ['Rescheduled', 'AGENDAMENTO REAGENDADO'],
  ['CheckedIn', 'CHEGADA CONFIRMADA'],
  ['Completed', 'ATENDIMENTO CONCLUÍDO'],
  ['NoShow', 'NÃO COMPARECEU'],
])('renders the actual %s reservation state', async (status, title) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({
    publicToken: 'reservation', confirmationCode: 'ABC', status,
    organizationName: 'Organization', branchName: 'Branch', serviceName: 'Service',
    scheduledStart: '2026-10-01T13:00:00Z', timeZone: 'UTC',
  })));
  const markup = renderToStaticMarkup(await AppointmentPage({ params: Promise.resolve({ publicToken: 'reservation' }) }));
  expect(markup).toContain(title);
  expect(markup).not.toContain('Aguardando confirmação');
  if (status !== 'Confirmed') expect(markup).not.toContain('AGENDAMENTO CONFIRMADO');
});
