import { attendantSession } from './session';
export async function post(path: string, request: Request) {
  return attendantSession.forward(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: (await request.text()) || '{}' });
}
