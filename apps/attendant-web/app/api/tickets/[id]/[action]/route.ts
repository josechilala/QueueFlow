import { post } from '../../../../../lib/api-proxy';
const actions = new Set(['start', 'complete', 'no-show']);
export async function POST(request: Request, context: { params: Promise<{ id: string; action: string }> }) { const { id, action } = await context.params; if (!actions.has(action)) return Response.json({}, { status: 404 }); return post(`/api/v1/tickets/${encodeURIComponent(id)}/${action}`, request); }
