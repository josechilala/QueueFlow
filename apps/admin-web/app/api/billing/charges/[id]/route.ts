import { tenantSession } from '../../../../../lib/session';
export async function GET(_request: Request, context: { params: Promise<{ id: string }> }) {
  const { id } = await context.params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) return new Response(null, { status: 400 });
  return tenantSession.forward(`/api/v1/billing/charges/${id}`, { method: 'GET' });
}
