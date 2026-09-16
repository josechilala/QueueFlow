import { configuredUrl } from '../../../../../lib/configured-url';
export async function POST(_: Request, context: { params: Promise<{ publicId: string }> }) {
  const { publicId } = await context.params;
  const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
  const response = await fetch(`${apiUrl}/api/v1/public/queues/${encodeURIComponent(publicId)}/tickets`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ priority: 'Normal' }), cache: 'no-store',
  });
  return new Response(await response.text(), { status: response.status, headers: { 'Content-Type': response.headers.get('Content-Type') ?? 'application/json' } });
}
