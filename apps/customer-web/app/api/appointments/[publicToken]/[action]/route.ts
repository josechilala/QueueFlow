import { proxyAppointment } from '../../../../../lib/appointment-proxy';
import { configuredUrl } from '../../../../../lib/configured-url';
const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
export async function POST(request: Request, { params }: { params: Promise<{ publicToken: string; action: string }> }) {
  const value = await params;
  if (!['cancel', 'reschedule'].includes(value.action)) return Response.json({ message: 'Ação inválida.' }, { status: 404 });
  if (value.action === 'reschedule') return proxyAppointment(request, value.publicToken);
  const response = await fetch(`${apiUrl}/api/v1/public/appointments/${encodeURIComponent(value.publicToken)}/${value.action}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: await request.text(), cache: 'no-store' });
  return new Response(await response.text(), { status: response.status, headers: { 'Content-Type': response.headers.get('Content-Type') ?? 'application/json' } });
}
