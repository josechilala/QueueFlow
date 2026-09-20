import { tenantSession } from './session';
export async function forwardAuthenticatedJson(request: Request, path: string, method: 'POST' | 'PUT' | 'PATCH') {
  return tenantSession.forward(path, { method, headers: { 'Content-Type': 'application/json' }, body: await request.text() });
}
export function forwardAuthenticatedDelete(path: string) {
  return tenantSession.forward(path, { method: 'DELETE' });
}
