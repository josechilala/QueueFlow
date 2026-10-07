import { afterEach, expect, it, vi } from 'vitest';
import { submitPlatformLogin } from './platform-login';

afterEach(() => vi.unstubAllGlobals());

it.each([
  [401, 'E-mail ou senha inválidos.'],
  [429, 'Excesso de tentativas'],
  [500, 'Serviço de autenticação indisponível'],
  [502, 'Serviço de autenticação indisponível'],
  [503, 'Serviço de autenticação indisponível'],
  [504, 'Serviço de autenticação indisponível'],
  [404, 'Não foi possível entrar'],
])('frontend classifies %s independently of an incorrect server message', async (status, expected) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ message: 'E-mail ou senha inválidos.' }, { status: Number(status) })));
  expect(await submitPlatformLogin('user@example.test', 'password')).toContain(expected);
});

it('reports a network failure as service unavailability', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network')));
  expect(await submitPlatformLogin('user@example.test', 'password')).toContain('Serviço de autenticação indisponível');
});

it('accepts only a confirmed authentication success', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json({ authenticated: true }));
  vi.stubGlobal('fetch', fetchMock);
  expect(await submitPlatformLogin('user@example.test', 'password')).toBeNull();
  expect(fetchMock.mock.calls[0][1]).toMatchObject({ headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': expect.stringMatching(/^[A-Za-z0-9_-]{1,64}$/) } });
});

it('rejects HTML with status 200 instead of redirecting to the panel', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>Error</html>')));
  expect(await submitPlatformLogin('user@example.test', 'password')).toContain('Serviço de autenticação indisponível');
});
