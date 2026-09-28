import { expect, it, vi } from 'vitest';
import { POST } from './route';
import { forwardAuthenticatedJson } from '../../../../../lib/server-api-proxy';

vi.mock('../../../../../lib/server-api-proxy', () => ({ forwardAuthenticatedJson: vi.fn() }));

it('rejects manual confirmation without forwarding it to the API', async () => {
  const response = await POST(new Request('https://admin.test/api/appointments/id/confirm', { method: 'POST' }), {
    params: Promise.resolve({ id: 'id', action: 'confirm' }),
  });
  expect(response.status).toBe(404);
  expect(forwardAuthenticatedJson).not.toHaveBeenCalled();
});
