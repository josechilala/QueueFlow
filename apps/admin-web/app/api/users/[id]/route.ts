import { forwardAuthenticatedJson } from '../../../../lib/server-api-proxy';
export async function PUT(request: Request, context: { params: Promise<{ id: string }> }) { const { id } = await context.params; return forwardAuthenticatedJson(request, `/api/v1/users/${encodeURIComponent(id)}`, 'PUT'); }
