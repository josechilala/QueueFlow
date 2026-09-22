// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { TrialRequestForm } from '../components/trial-request-form';

let container: HTMLDivElement;
let root: Root;
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubEnv('NEXT_PUBLIC_QUEUEFLOW_API_URL', 'https://api.example.test');
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  act(() => root.render(<TrialRequestForm />));
  for (const [name, value] of Object.entries({ name: ' Test Owner ', email: 'owner@example.test', companyName: ' Test Company ', phone: '+55 (11) 99999-1234' })) {
    (container.querySelector(`[name="${name}"]`) as HTMLInputElement).value = value;
  }
  (container.querySelector('[name="acceptedTerms"]') as HTMLInputElement).checked = true;
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.unstubAllEnvs(); });
const submit = () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

it('requires consent before sending any personal information', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  (container.querySelector('[name="acceptedTerms"]') as HTMLInputElement).checked = false;
  await act(async () => { submit(); });
  expect(fetch).not.toHaveBeenCalled();
});

it('sends only contact data and consent once, then confirms receipt without automatic approval', async () => {
  let complete!: (response: Response) => void;
  const fetch = vi.fn(() => new Promise<Response>(resolve => { complete = resolve; }));
  vi.stubGlobal('fetch', fetch);
  await act(async () => { submit(); submit(); });
  expect(fetch).toHaveBeenCalledTimes(1);
  const [url, options] = fetch.mock.calls[0] as unknown as [string, RequestInit];
  expect(url).toBe('https://api.example.test/api/v1/public/trial-requests');
  expect(options.credentials).toBe('omit');
  expect(JSON.parse(options.body as string)).toEqual({ name: 'Test Owner', email: 'owner@example.test', companyName: 'Test Company', phone: '+55 (11) 99999-1234', acceptedTerms: true });
  expect((container.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
  await act(async () => { complete(new Response('{}', { status: 202 })); });
  expect(container.querySelector('[role="status"]')?.textContent).toContain('Vamos analisar seus dados');
  expect(container.querySelector('form')).toBeNull();
});

it.each([400, 429, 503])('shows a recoverable error for HTTP %i without losing fields', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'Confira os campos.' }), { status })));
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toBeTruthy();
  expect((container.querySelector('[name="companyName"]') as HTMLInputElement).value).toBe(' Test Company ');
  expect((container.querySelector('button') as HTMLButtonElement).disabled).toBe(false);
  expect(container.textContent).not.toContain('Solicitação recebida.');
});

it('allows retry after a network failure without reporting success', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Network error')));
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Não foi possível confirmar');
  expect((container.querySelector('button') as HTMLButtonElement).disabled).toBe(false);
});
