// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { LoginForm } from './login-form';

const navigation = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn(), search: new URLSearchParams() }));
vi.mock('next/navigation', () => ({ useRouter: () => navigation, useSearchParams: () => navigation.search }));
let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  navigation.replace.mockReset(); navigation.refresh.mockReset(); navigation.search = new URLSearchParams();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  act(() => root.render(<StrictMode><LoginForm /></StrictMode>));
  container.querySelector<HTMLInputElement>('[name="email"]')!.value = 'owner@example.test';
  container.querySelector<HTMLInputElement>('[name="password"]')!.value = 'password with spaces';
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });
const submit = () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
const button = () => container.querySelector('button')!;
const advance = async (ms: number) => { await act(async () => { vi.advanceTimersByTime(ms); }); };

it('one click generates one request; rapid submits remain single-flight through slow startup and navigation', async () => {
  let finish!: (response: Response) => void;
  const fetchMock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve; }));
  vi.stubGlobal('fetch', fetchMock);
  expect(fetchMock).not.toHaveBeenCalled();
  await act(async () => { button().click(); submit(); submit(); });
  await advance(60_000);
  await act(async () => { submit(); button().click(); });
  expect(fetchMock).toHaveBeenCalledTimes(1); expect(button().disabled).toBe(true);
  await act(async () => { finish(Response.json({ authenticated: true, role: 'Owner', needsOnboarding: false })); });
  expect(navigation.replace).toHaveBeenCalledWith('/dashboard');
  await act(async () => { submit(); }); expect(fetchMock).toHaveBeenCalledTimes(1);
});

it.each(['12', 'date', 'missing', 'invalid'])('honors Retry-After %s, including submit events while cooling down, without automatic retries', async header => {
  const seconds = header === 'missing' || header === 'invalid' ? 60 : 12;
  const headers = header === 'missing' ? {} : { 'Retry-After': header === 'date' ? new Date(Date.now() + 12_000).toUTCString() : header };
  const fetchMock = vi.fn().mockResolvedValue(new Response('<html>Rate limited</html>', { status: 429, headers }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Excesso de tentativas');
  await advance((seconds - 1) * 1000);
  await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(1); expect(button().disabled).toBe(true);
  await advance(1000); expect(button().disabled).toBe(false); expect(fetchMock).toHaveBeenCalledTimes(1);
  await act(async () => { submit(); }); expect(fetchMock).toHaveBeenCalledTimes(2);
});

it.each([502, 503, 504])('HTTP %i pauses without a request storm and a later manual attempt succeeds', async status => {
  const fetchMock = vi.fn().mockResolvedValueOnce(new Response('gateway HTML', { status }))
    .mockResolvedValueOnce(Response.json({ authenticated: true, role: 'Owner', needsOnboarding: true }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('temporariamente indisponível');
  await act(async () => { submit(); submit(); }); expect(fetchMock).toHaveBeenCalledTimes(1);
  await advance(5000); expect(fetchMock).toHaveBeenCalledTimes(1);
  await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(2); expect(navigation.replace).toHaveBeenCalledWith('/onboarding');
});

it('respects Retry-After for temporary infrastructure errors as well', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 503, headers: { 'Retry-After': '20' } }));
  vi.stubGlobal('fetch', fetchMock); await act(async () => { submit(); });
  await advance(5000); await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(1); expect(button().disabled).toBe(true);
  await advance(15000); expect(button().disabled).toBe(false);
});

it.each(['network', 'invalid JSON'])('recovers from %s and preserves returnTo', async kind => {
  navigation.search = new URLSearchParams('returnTo=/services?branch=one');
  const fetchMock = vi.fn();
  if (kind === 'network') fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
  else fetchMock.mockResolvedValueOnce(new Response('<html>Loading</html>'));
  fetchMock.mockResolvedValueOnce(Response.json({ authenticated: true, role: 'Admin', needsOnboarding: false }));
  vi.stubGlobal('fetch', fetchMock); await act(async () => { submit(); });
  expect(navigation.replace).not.toHaveBeenCalled();
  await advance(5000); await act(async () => { submit(); });
  expect(navigation.replace).toHaveBeenCalledWith('/services?branch=one');
});

it('shows invalid credentials separately and does not erase existing browser state', async () => {
  document.cookie = 'existing_session=preserved';
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
  vi.stubGlobal('fetch', fetchMock); await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toBe('E-mail ou senha inválidos.');
  expect(button().disabled).toBe(false); expect(document.cookie).toContain('existing_session=preserved');
  await advance(120_000); expect(fetchMock).toHaveBeenCalledTimes(1);
});
