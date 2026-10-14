// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { BillingCheckout, type BillingPlan } from './billing-checkout';

const plans: BillingPlan[] = [{ code: 'professional', name: 'Profissional', monthlyPrice: 69.9,
  annualPrice: 796.86, includedAttendants: 4, currency: 'BRL', annualDiscountPercent: 5 }];
let root: Root; let container: HTMLDivElement;
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  localStorage.clear();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
const mount = async () => { await act(async () => { root.render(<BillingCheckout plans={plans} />); }); };
const submit = () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
const fill = () => {
  container.querySelector<HTMLInputElement>('[name="customerName"]')!.value = 'Sandbox owner';
  container.querySelector<HTMLInputElement>('[name="customerEmail"]')!.value = 'owner@example.test';
  container.querySelector<HTMLInputElement>('[name="cpfCnpj"]')!.value = '12345678901';
};

it('refreshes Uncertain to Cancelled with independent loading and a new key for the next PIX', async () => {
  const id = '93985839-3250-4085-b587-03e29186568b';
  let finishLookup!: (value: Response) => void;
  const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({ checkoutEnabled: true }))
    .mockResolvedValueOnce(Response.json({ id, status: 'Uncertain', amount: 69.9, currency: 'BRL' }))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { finishLookup = resolve; }))
    .mockResolvedValueOnce(Response.json({ id: '11111111-1111-1111-1111-111111111111', status: 'Pending',
      amount: 69.9, currency: 'BRL', pixPayload: 'test-copy-paste', pixEncodedImage: 'dGVzdA==' }));
  vi.stubGlobal('fetch', fetchMock); await mount(); fill();
  await act(async () => { submit(); });
  const firstKey = JSON.parse(fetchMock.mock.calls[1][1].body).idempotencyKey;
  const lookup = [...container.querySelectorAll('button')].find(button => button.textContent === 'Consultar cobrança')!;
  await act(async () => { lookup.click(); });
  expect(container.querySelector('[type="submit"]')!.textContent).toBe('Iniciar pagamento');
  expect(lookup.disabled).toBe(true);
  expect(fetchMock.mock.calls[2][0]).toBe(`/api/billing/charges/${id}`);
  expect(fetchMock.mock.calls[2][1]).not.toHaveProperty('body');
  await act(async () => { finishLookup(Response.json({ id, status: 'Cancelled', amount: 69.9, currency: 'BRL' })); });
  expect(container.textContent).toContain('Cobrança cancelada');
  expect(container.textContent).not.toContain('Não inicie outra cobrança');
  expect(container.querySelector('fieldset')!.disabled).toBe(false);
  await act(async () => { submit(); });
  expect(JSON.parse(fetchMock.mock.calls[3][1].body).idempotencyKey).not.toBe(firstKey);
  expect(container.querySelector('img')!.getAttribute('src')).toBe('data:image/png;base64,dGVzdA==');
  expect(container.querySelector('textarea')!.value).toBe('test-copy-paste');
  expect(container.textContent).toContain('Aguardando pagamento');
  expect(container.textContent).toContain('69,90');
});

it('reloads a saved cancelled reservation from the server without creating a checkout', async () => {
  const id = '93985839-3250-4085-b587-03e29186568b';
  localStorage.setItem('queueflow.billing.charge', id);
  const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({ checkoutEnabled: true }))
    .mockResolvedValueOnce(Response.json({ id, status: 'Cancelled', amount: 69.9, currency: 'BRL' }));
  vi.stubGlobal('fetch', fetchMock); await mount();
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(fetchMock.mock.calls[1][0]).toBe(`/api/billing/charges/${id}`);
  expect(container.textContent).toContain('Cobrança cancelada');
  expect(container.querySelector('fieldset')!.disabled).toBe(false);
});

it('cannot submit while the server has not enabled checkout', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json({ checkoutEnabled: false }));
  vi.stubGlobal('fetch', fetchMock); await mount();
  await act(async () => { submit(); });
  expect(container.querySelector<HTMLButtonElement>('[type="submit"]')!.disabled).toBe(true);
  expect(fetchMock).toHaveBeenCalledTimes(1);
});

it('offers session recovery on 401 without claiming checkout is disabled', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
  await mount();
  expect(container.querySelector('a')?.getAttribute('href')).toBe('/api/auth/refresh?returnTo=/billing');
  expect(container.textContent).not.toContain('Contratação em preparação');
  expect(container.querySelector<HTMLButtonElement>('[type="submit"]')!.disabled).toBe(true);
});

it('distinguishes an unavailable API from a disabled checkout', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })));
  await mount();
  expect(container.textContent).toContain('Não foi possível consultar a disponibilidade');
  expect(container.textContent).not.toContain('Contratação em preparação');
});

it('reuses the request key after a lost response and never sends a price or tenant ID', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(Response.json({ checkoutEnabled: true }))
    .mockRejectedValueOnce(new Error('connection lost'))
    .mockResolvedValueOnce(Response.json({ id: '12345678-1234-1234-1234-123456789012', status: 'Uncertain', amount: 69.9, currency: 'BRL' }));
  vi.stubGlobal('fetch', fetchMock); await mount(); fill();
  await act(async () => { submit(); });
  await act(async () => { submit(); });
  const first = JSON.parse(fetchMock.mock.calls[1][1].body);
  const second = JSON.parse(fetchMock.mock.calls[2][1].body);
  expect(second.idempotencyKey).toBe(first.idempotencyKey);
  expect(first).not.toHaveProperty('amount'); expect(first).not.toHaveProperty('organizationId');
  expect(first).not.toHaveProperty('creditCard');
  expect(container.textContent).toContain('Não inicie outra cobrança');
});

it('shows the annual upfront total and excludes debit from payment options', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ checkoutEnabled: false })));
  await mount();
  const selects = container.querySelectorAll('select');
  await act(async () => { selects[1].value = 'Yearly'; selects[1].dispatchEvent(new Event('change', { bubbles: true })); });
  expect(container.textContent).toContain('796,86');
  expect(container.textContent).toContain('valor integral antecipado');
  expect([...selects[2].options].map(value => value.value)).toEqual(['Pix', 'CreditCard']);
});

it('lets platform admins inspect payment options without calling tenant checkout endpoints', async () => {
  const fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
  await act(async () => { root.render(<BillingCheckout plans={plans} previewOnly />); });
  const selects = container.querySelectorAll('select');
  expect([...selects[2].options].map(value => value.value)).toEqual(['Pix', 'CreditCard']);
  await act(async () => { selects[1].value = 'Yearly'; selects[1].dispatchEvent(new Event('change', { bubbles: true })); submit(); });
  expect(container.textContent).toContain('796,86');
  expect(container.querySelector('[type="submit"]')).toBeNull();
  expect(container.querySelector('[name="cpfCnpj"]')).toBeNull();
  expect(fetchMock).not.toHaveBeenCalled();
});
