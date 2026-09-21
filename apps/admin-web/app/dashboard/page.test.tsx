import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import DashboardPage from './page';
import { getServerSession } from '../../lib/server-session';
import { getDashboardSummary } from '../../lib/server-dashboard';
import type { DashboardSummary } from '../../lib/dashboard';

vi.mock('../../lib/server-session', () => ({ getServerSession: vi.fn() }));
vi.mock('../../lib/server-dashboard', () => ({ getDashboardSummary: vi.fn() }));
vi.mock('../../lib/server-onboarding', () => ({ getOnboardingProgress: () => { throw new Error('Dashboard must not require onboarding'); } }));
vi.mock('../../components/realtime-refresh', () => ({ RealtimeRefresh: () => null }));
vi.mock('./logout-button', () => ({ LogoutButton: () => null }));
vi.mock('next/navigation', () => ({ redirect: (path: string) => { throw new Error(`redirect:${path}`); } }));
vi.mock('../../lib/server-access-links', () => ({ getConfiguredAccessOrigins: () => ({
  admin: 'https://admin.test', attendant: 'https://attendant.test', customer: 'https://customer.test', display: null,
}) }));

const summary: DashboardSummary = {
  organizationSlug: 'tenant-company', activeQueues: 2, waiting: 7, inService: 3,
  appointmentsToday: 11, upcomingAppointments: 18, activeBranches: 4,
  completedToday: 0, averageWaitMinutes: 0, generatedAt: '2026-09-22T12:00:00Z', queuesInProgress: [],
  queues: [{ id: 'queue', name: 'Fila existente', branchName: 'Unidade Centro', serviceName: 'Consulta',
    status: 'Closed', waiting: 0, activeAttendants: 0, estimatedWaitMinutes: 0 }],
};

beforeEach(() => {
  vi.stubGlobal('React', React);
  vi.mocked(getServerSession).mockResolvedValue({ status: 200, user: {
    userId: 'owner', organizationId: 'tenant', name: 'Owner', email: 'owner@example.test', role: 'Owner',
  } });
  vi.mocked(getDashboardSummary).mockResolvedValue({ status: 200, summary });
});
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

it('renders real summary metrics, all queues and existing access links without onboarding', async () => {
  const html = renderToStaticMarkup(await DashboardPage());
  for (const [label, value] of [['Filas abertas', 2], ['Clientes aguardando', 7], ['Clientes em atendimento', 3],
    ['Agendamentos de hoje', 11], ['Próximos agendamentos', 18], ['Unidades ativas', 4]]) {
    expect(html).toContain(`<p>${label}</p><strong>${value}</strong>`);
  }
  for (const text of ['Links de acesso', 'Painel do atendente', 'Agendamento público',
    'https://attendant.test', 'https://customer.test/agendamento/tenant-company',
    'Fila existente', 'Unidade Centro', 'Consulta', 'Fechada']) expect(html).toContain(text);
  expect(html).not.toContain('/onboarding');
  expect(html).not.toContain('Próxima etapa');
  expect(html).not.toContain('Crie a primeira unidade');
});

it('renders an empty tenant without invented data', async () => {
  vi.mocked(getDashboardSummary).mockResolvedValue({ status: 200, summary: {
    ...summary, activeQueues: 0, waiting: 0, inService: 0, appointmentsToday: 0,
    upcomingAppointments: 0, activeBranches: 0, queues: [],
  } });
  const html = renderToStaticMarkup(await DashboardPage());
  expect(html).toContain('Nenhuma fila cadastrada.');
  expect(html.match(/<strong>0<\/strong>/g)).toHaveLength(6);
  expect(html).toContain('Links de acesso');
});

it('renews an expired session before loading tenant data', async () => {
  vi.mocked(getServerSession).mockResolvedValue({ status: 401 });
  await expect(DashboardPage()).rejects.toThrow('redirect:/api/auth/refresh?returnTo=/dashboard');
  expect(getDashboardSummary).not.toHaveBeenCalled();
});

it('preserves forbidden responses', async () => {
  vi.mocked(getDashboardSummary).mockResolvedValue({ status: 403 });
  expect(renderToStaticMarkup(await DashboardPage())).toContain('Acesso não permitido');
});

it('does not replace an API outage with zero metrics', async () => {
  vi.mocked(getDashboardSummary).mockResolvedValue({ status: 503 });
  await expect(DashboardPage()).rejects.toThrow('Não foi possível carregar o resumo administrativo.');
});
