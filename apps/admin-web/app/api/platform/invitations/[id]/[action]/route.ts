import { forwardPlatformJson } from '../../../../../../lib/server-platform-proxy';
export async function POST(request: Request, { params }: { params: Promise<{ id: string; action: string }> }) {
  const { id, action } = await params;
  if (action !== 'revoke' && action !== 'reissue' && action !== 'send') return Response.json({ message: 'Ação inválida.' }, { status: 404 });
  return forwardPlatformJson(request, `/api/v1/platform/invitations/${id}/${action}`, 'POST');
}
