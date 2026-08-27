import { post } from '../../../../../lib/api-proxy';
export async function POST(request: Request, context: { params: Promise<{ id: string }> }) { const { id } = await context.params; return post(`/api/v1/queues/${encodeURIComponent(id)}/call-next`, request); }
