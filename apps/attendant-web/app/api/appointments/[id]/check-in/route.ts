import { post } from '../../../../../lib/api-proxy';

export async function POST(request: Request, context: { params: Promise<{ id: string }> }) {
  const { id } = await context.params;
  return post(`/api/v1/operations/appointments/${encodeURIComponent(id)}/check-in`, request);
}
