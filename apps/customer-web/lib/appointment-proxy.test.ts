import { afterEach, describe, expect, it, vi } from 'vitest';
import { POST as create } from '../app/api/appointments/route';
import { POST as action } from '../app/api/appointments/[publicToken]/[action]/route';

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('appointment proxy diagnostics', () => {
  it.each([200, 201, 400, 404, 409, 422, 429, 500, 502, 503, 504])('preserves status %i and body with exactly one upstream POST', async status => {
    const body = JSON.stringify({ detail: 'Resposta da API' });
    const fetch = vi.fn().mockResolvedValue(new Response(body, { status, headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '45' } }));
    vi.stubGlobal('fetch', fetch);
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const payload = JSON.stringify({ branchPublicId: 'branch', servicePublicId: 'service', scheduledStart: '2026-09-24T13:00:00+00:00', customerName: 'Private name', customerEmail: 'private@example.test', customerPhone: null });
    const response = await create(new Request('https://customer.test/api/appointments', { method: 'POST', body: payload }));
    expect(response.status).toBe(status);
    expect(await response.text()).toBe(body);
    expect(response.headers.get('Content-Type')).toBe('application/problem+json');
    expect(response.headers.get('Retry-After')).toBe('45');
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toMatch(/\/api\/v1\/public\/appointments$/);
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: 'POST', body: payload, headers: { 'X-Correlation-ID': response.headers.get('X-Correlation-ID') } });
  });

  it('correlates reschedule failures without logging the token, payload or upstream error', async () => {
    const log = vi.spyOn(console, 'error').mockImplementation(() => {});
    const fetch = vi.fn().mockResolvedValue(new Response('sensitive upstream error', { status: 500 }));
    vi.stubGlobal('fetch', fetch);
    const response = await action(new Request('https://customer.test/api/appointments/secret/reschedule', { method: 'POST', body: '{"scheduledStart":"2026-09-24T13:00:00Z"}' }), { params: Promise.resolve({ publicToken: 'secret/token', action: 'reschedule' }) });
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toMatch(/\/appointments\/secret%2Ftoken\/reschedule$/);
    expect(log).toHaveBeenCalledWith('appointment_proxy_failure', expect.objectContaining({ operation: 'reschedule', endpoint: '/api/v1/public/appointments/{publicToken}/reschedule', correlationId: response.headers.get('X-Correlation-ID'), stage: 'api_response', status: 500 }));
    expect(JSON.stringify(log.mock.calls)).not.toMatch(/secret|sensitive|scheduledStart/);
  });

  it('does not retry or log exception messages on a transport failure', async () => {
    const failure = new TypeError('private@example.test secret/token');
    const fetch = vi.fn().mockRejectedValue(failure);
    vi.stubGlobal('fetch', fetch);
    const log = vi.spyOn(console, 'error').mockImplementation(() => {});
    await expect(create(new Request('https://customer.test/api/appointments', { method: 'POST', body: '{}' }))).rejects.toBe(failure);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(log).toHaveBeenCalledWith('appointment_proxy_failure', expect.objectContaining({ stage: 'api_request', exceptionType: 'TypeError' }));
    expect(JSON.stringify(log.mock.calls)).not.toMatch(/private|secret/);
  });

  it('preserves a non-JSON response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<h1>Unavailable</h1>', { status: 503, headers: { 'Content-Type': 'text/html' } })));
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const response = await create(new Request('https://customer.test/api/appointments', { method: 'POST', body: '{}' }));
    expect(response.status).toBe(503);
    expect(response.headers.get('Content-Type')).toBe('text/html');
    expect(await response.text()).toBe('<h1>Unavailable</h1>');
  });
});
