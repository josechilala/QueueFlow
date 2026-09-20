import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ActivationFlow } from './[token]/activation-flow';
import ActivationPage from './page';

const token = 'a'.repeat(64);
const grant = 'b'.repeat(64);
const email = 'owner+test@example.test';
const invitation = { maskedEmail: 'ow***@example.test', organizationName: 'Test Company', status: 'Pending', canRequestVerificationCode: true, isVerified: false };
let container: HTMLDivElement;
let root: Root;
let fetchMock: ReturnType<typeof vi.fn>;
let onComplete: () => Promise<Response>;

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
const input = (name: string) => container.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
const completionForm = () => input('password').form!;
const successPanel = () => Array.from(container.querySelectorAll('h1')).find(node => node.textContent === 'Conta ativada')!.parentElement!;
const completionCalls = () => fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/complete'));
const submit = (form: HTMLFormElement) => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

async function showForm() {
  await act(async () => root.render(<ActivationFlow token={token} />));
  input('code').value = '123456';
  await act(async () => { submit(input('code').form!); });
  expect(completionForm().parentElement!.hidden).toBe(false);
  input('responsibleName').value = 'Test Owner';
  input('password').value = 'Synthetic-password-123!';
}

// Translators can replace React's text nodes with wrappers and new text nodes.
// This deliberately invalidates the original text-node references as in the reported failure.
function translateDom() {
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  const nodes: Node[] = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  for (const node of nodes) {
    const font = document.createElement('font');
    font.textContent = node.textContent;
    node.parentNode!.replaceChild(font, node);
  }
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  onComplete = async () => Response.json({ organizationId: 'organization-id', email });
  fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith('/lookup')) return Response.json(invitation);
    if (url.endsWith('/verify')) return Response.json({ activationAuthorization: grant });
    if (url.endsWith('/complete')) return onComplete();
    if (url.endsWith('/request-code')) return Response.json({ delivered: true });
    throw new Error('Unexpected endpoint');
  });
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('activation completion', () => {
  it('keeps panels mounted and shows the Admin login link after HTTP 200', async () => {
    await showForm();
    const form = completionForm();
    const success = successPanel();
    const section = container.firstElementChild;
    await act(async () => { submit(form); });
    expect(container.firstElementChild).toBe(section);
    expect(completionForm()).toBe(form);
    expect(successPanel()).toBe(success);
    expect(form.parentElement!.hidden).toBe(true);
    expect(success.hidden).toBe(false);
    const link = success.querySelector('a')!;
    const url = new URL(link.href);
    expect(url.origin).toBe('https://admin.example.test');
    expect(url.pathname).toBe('/login');
    expect(url.searchParams.get('email')).toBe(email);
    expect(url.searchParams.get('activated')).toBe('1');
    expect(input('password').value).toBe('');
    const body = JSON.parse(completionCalls()[0][1].body);
    expect(body).toMatchObject({ invitationToken: token, activation: { activationAuthorization: grant, organizationName: 'Test Company', responsibleName: 'Test Owner', password: 'Synthetic-password-123!' } });
  });

  it('survives translator DOM mutations during the successful transition', async () => {
    await showForm();
    const form = completionForm();
    const success = successPanel();
    translateDom();
    await act(async () => { submit(form); });
    expect(successPanel()).toBe(success);
    expect(success.hidden).toBe(false);
    expect(completionForm()).toBe(form);
    expect(form.parentElement!.hidden).toBe(true);
    expect(success.querySelector('a')!.hidden).toBe(false);
    expect(completionCalls()).toHaveLength(1);
  });

  it('blocks two submits in the same event batch, while pending, and after success', async () => {
    const response = deferred<Response>();
    onComplete = () => response.promise;
    await showForm();
    const form = completionForm();
    await act(async () => { submit(form); submit(form); });
    expect(completionCalls()).toHaveLength(1);
    expect(form.querySelector('button')!.disabled).toBe(true);
    await act(async () => { submit(form); });
    expect(completionCalls()).toHaveLength(1);
    await act(async () => response.resolve(Response.json({ organizationId: 'organization-id', email })));
    await act(async () => { submit(form); });
    expect(completionCalls()).toHaveLength(1);
    expect(successPanel().hidden).toBe(false);
  });

  it('confirms a committed activation before reading the body and never reopens it on body failure', async () => {
    const body = deferred<unknown>();
    onComplete = async () => ({ status: 200, json: () => body.promise }) as Response;
    await showForm();
    await act(async () => { submit(completionForm()); });
    expect(successPanel().hidden).toBe(false);
    expect(new URL(successPanel().querySelector('a')!.href).pathname).toBe('/login');
    await act(async () => { submit(completionForm()); });
    expect(completionCalls()).toHaveLength(1);
    await act(async () => body.reject(new TypeError('connection lost while reading JSON')));
    expect(successPanel().hidden).toBe(false);
    expect(completionForm().parentElement!.hidden).toBe(true);
  });

  it('preserves success even when the HTTP 200 body is malformed', async () => {
    onComplete = async () => new Response('<html>invalid JSON</html>', { status: 200 });
    await showForm();
    await act(async () => { submit(completionForm()); });
    expect(successPanel().hidden).toBe(false);
    expect(successPanel().querySelector('a')!.href).toBe('https://admin.example.test/login?activated=1');
  });

  it('keeps a rejected activation editable and allows a deliberate retry', async () => {
    onComplete = async () => Response.json({ detail: 'Slug already in use.' }, { status: 400 });
    await showForm();
    await act(async () => { submit(completionForm()); });
    expect(successPanel().hidden).toBe(true);
    expect(completionForm().parentElement!.hidden).toBe(false);
    expect(completionForm().querySelector('button')!.disabled).toBe(false);
    expect(container.textContent).toContain('Slug already in use.');
    onComplete = async () => Response.json({ organizationId: 'organization-id', email });
    await act(async () => { submit(completionForm()); });
    expect(successPanel().hidden).toBe(false);
    expect(completionCalls()).toHaveLength(2);
  });

  it('requests verification again after an expired grant without reusing it', async () => {
    onComplete = async () => Response.json({ detail: 'Invitation unavailable.' }, { status: 404 });
    await showForm();
    await act(async () => { submit(completionForm()); });
    expect(completionForm().parentElement!.hidden).toBe(true);
    expect(input('code').form!.parentElement!.hidden).toBe(false);
    await act(async () => { submit(completionForm()); });
    expect(completionCalls()).toHaveLength(1);
  });

  it('reports an unknown outcome on network failure without automatically repeating completion', async () => {
    onComplete = async () => { throw new TypeError('network failure'); };
    await showForm();
    await act(async () => { submit(completionForm()); });
    expect(successPanel().hidden).toBe(true);
    expect(container.textContent).toContain('Verifique seu acesso ao Admin antes de tentar novamente.');
    expect(completionCalls()).toHaveLength(1);
  });

  it('captures the invitation fragment in /ativar and does not send it in a request URL', async () => {
    window.history.replaceState(null, '', '/ativar#' + token);
    await act(async () => root.render(<ActivationPage />));
    expect(window.location.hash).toBe('');
    const lookup = fetchMock.mock.calls.find(([url]) => url.endsWith('/lookup'))!;
    expect(lookup[0]).toBe('https://api.example.test/api/v1/public/activation/lookup');
    expect(JSON.parse(lookup[1].body)).toEqual({ invitationToken: token });
  });
});
