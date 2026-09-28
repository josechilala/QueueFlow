import { forwardAuthenticatedJson } from '../../../../../lib/server-api-proxy';

type RouteContext = { params: Promise<{ id: string; action: string }> };

export async function POST(request: Request, { params }: RouteContext) {
  const { id, action } = await params;
  if (action !== 'cancel' && action !== 'no-show') {
    return Response.json({ message: 'Ação inválida.' }, { status: 404 });
  }

  return forwardAuthenticatedJson(request, `/api/v1/appointments/${id}/${action}`, 'POST');
}
