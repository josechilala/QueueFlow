import { forwardAuthenticatedJson } from '../../../../../lib/server-api-proxy';

const allowedActions = new Set(['open', 'pause', 'close']);
export async function POST(request: Request, context: { params: Promise<{ id: string; action: string }> }) {
  const { id, action } = await context.params;
  if (!allowedActions.has(action)) return Response.json({ message: 'Ação inválida.' }, { status: 404 });
  return forwardAuthenticatedJson(request, `/api/v1/queues/${encodeURIComponent(id)}/${action}`, 'POST');
}
