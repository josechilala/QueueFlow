import { forwardPlatformJson } from '../../../../../../lib/server-platform-proxy';
import { publicUrl } from '../../../../../../lib/public-url';

export async function POST(request: Request, { params }: { params: Promise<{ id: string; action: string }> }) {
  if (request.headers.get('origin') !== publicUrl(request, '/').origin) return Response.json({ message: 'Origem inválida.' }, { status: 403 });
  const { id, action } = await params;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || (action !== 'approve' && action !== 'reject')) return Response.json({ message: 'Ação inválida.' }, { status: 404 });
  return forwardPlatformJson(request, `/api/v1/platform/trial-requests/${id}/${action}`, 'POST');
}
