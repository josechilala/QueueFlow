import { platformSession } from './session';
export async function forwardPlatformJson(request: Request, path: string, method: 'POST') {
  return platformSession.forward(path, { method, headers: { 'Content-Type': 'application/json' }, body: await request.text() });
}
