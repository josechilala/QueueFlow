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
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  navigation.replace.mockReset(); navigation.refresh.mockReset(); navigation.search = new URLSearchParams();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  act(() => root.render(<StrictMode><LoginForm /></StrictMode>));
  container.querySelector<HTMLInputElement>('[name="email"]')!.value = 'owner@example.test';
  container.querySelector<HTMLInputElement>('[name="password"]')!.value = 'password with spaces';
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
const submit = () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
const button = () => container.querySelector('button')!;

it('keeps rapid submits single-flight while one login request is pending', async () => {
  let finish!: (response: Response) => void;
  const fetchMock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve; }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { button().click(); submit(); submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(1);
  expect(button().disabled).toBe(true);
  await act(async () => { finish(Response.json({ authenticated: true, role: 'Admin' })); });
  expect(navigation.replace).toHaveBeenCalledWith('/dashboard');
  expect(navigation.refresh).toHaveBeenCalledTimes(1);
  await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(1);
});

it('does not create a browser cooldown after 429; a manual retry is immediate', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(new Response(null, { status: 429, headers: { 'Retry-After': '60' } }))
    .mockResolvedValueOnce(Response.json({ authenticated: true, role: 'Admin' }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('Excesso de tentativas');
  expect(button().disabled).toBe(false);
  await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(navigation.replace).toHaveBeenCalledWith('/dashboard');
});

it.each([502, 503, 504])('allows an immediate explicit retry after HTTP %i', async status => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(new Response(null, { status }))
    .mockResolvedValueOnce(Response.json({ authenticated: true, role: 'Admin' }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toContain('temporariamente indisponível');
  expect(button().disabled).toBe(false);
  await act(async () => { submit(); });
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(navigation.replace).toHaveBeenCalledWith('/dashboard');
});

it('routes Owner through post-login bootstrap and preserves returnTo', async () => {
  navigation.search = new URLSearchParams('returnTo=/services?branch=one');
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ authenticated: true, role: 'Owner' })));
  await act(async () => { submit(); });
  expect(navigation.replace).toHaveBeenCalledWith('/post-login?returnTo=%2Fservices%3Fbranch%3Done');
});

it('routes non-Owner admin roles directly to the safe returnTo', async () => {
  navigation.search = new URLSearchParams('returnTo=/services?branch=one');
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ authenticated: true, role: 'Manager' })));
  await act(async () => { submit(); });
  expect(navigation.replace).toHaveBeenCalledWith('/services?branch=one');
});

it.each(['network', 'invalid JSON'])('recovers from %s on a later explicit submit', async kind => {
  const fetchMock = vi.fn();
  if (kind === 'network') fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
  else fetchMock.mockResolvedValueOnce(new Response('<html>Loading</html>'));
  fetchMock.mockResolvedValueOnce(Response.json({ authenticated: true, role: 'Admin' }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(navigation.replace).not.toHaveBeenCalled();
  expect(button().disabled).toBe(false);
  await act(async () => { submit(); });
  expect(navigation.replace).toHaveBeenCalledWith('/dashboard');
});

it('shows invalid credentials separately and preserves existing browser state', async () => {
  document.cookie = 'existing_session=preserved';
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { submit(); });
  expect(container.querySelector('[role="alert"]')?.textContent).toBe('E-mail ou senha inválidos.');
  expect(button().disabled).toBe(false);
  expect(document.cookie).toContain('existing_session=preserved');
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
