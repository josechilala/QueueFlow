import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { BookingForm } from './[branchPublicId]/[servicePublicId]/booking-form';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
afterEach(() => { document.body.innerHTML = ''; vi.restoreAllMocks(); });

it('requires a valid customer email before a new booking can be submitted', async () => {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  await act(async () => root.render(<BookingForm organizationName="Empresa" branchName="Unidade" serviceName="Serviço"
    branchPublicId="branch" servicePublicId="service" timeZone="UTC"
    slots={[{ startAt: '2026-12-01T10:00:00Z', endAt: '2026-12-01T10:30:00Z', remainingCapacity: 1 }]} />));
  const email = container.querySelector<HTMLInputElement>('input[name="customerEmail"]')!;
  expect(email.required).toBe(true);
  expect(email.checkValidity()).toBe(false);
  email.value = 'invalid'; expect(email.checkValidity()).toBe(false);
  email.value = 'customer@example.test'; expect(email.checkValidity()).toBe(true);
  await act(async () => root.unmount());
});
