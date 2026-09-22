import { beforeEach, expect, it, vi } from 'vitest';
import { POST } from './route';
import { forwardPlatformJson } from '../../../../../../lib/server-platform-proxy';

vi.mock('../../../../../../lib/server-platform-proxy', () => ({ forwardPlatformJson: vi.fn() }));
vi.mock('../../../../../../lib/public-url', () => ({ publicUrl: () => new URL('https://admin.example.test/') }));
const id = 'f6f5a248-1b20-4f7f-87bb-987ce81f7901';
beforeEach(() => { vi.clearAllMocks(); vi.mocked(forwardPlatformJson).mockResolvedValue(Response.json({ id })); });
function request(origin?: string) {
  return new Request('https://admin.example.test/api/platform/trial-requests/' + id + '/approve', { method: 'POST', headers: origin ? { origin } : {}, body: '{}' });
}

it.each([undefined, 'https://attacker.example.test'])('blocks untrusted origin %s before accessing the platform session', async origin => {
  expect((await POST(request(origin), { params: Promise.resolve({ id, action: 'approve' }) })).status).toBe(403);
  expect(forwardPlatformJson).not.toHaveBeenCalled();
});
it.each(['approve', 'reject'])('uses the existing platform session for %s', async action => {
  const req = request('https://admin.example.test');
  expect((await POST(req, { params: Promise.resolve({ id, action }) })).status).toBe(200);
  expect(forwardPlatformJson).toHaveBeenCalledExactlyOnceWith(req, `/api/v1/platform/trial-requests/${id}/${action}`, 'POST');
});
it.each([{ id: '../invitations', action: 'approve' }, { id, action: 'delete' }])('rejects invalid route parameters %j', async params => {
  expect((await POST(request('https://admin.example.test'), { params: Promise.resolve(params) })).status).toBe(404);
  expect(forwardPlatformJson).not.toHaveBeenCalled();
});
