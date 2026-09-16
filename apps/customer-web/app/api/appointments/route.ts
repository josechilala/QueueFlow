import { configuredUrl } from '../../../lib/configured-url';
const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
export async function POST(request: Request) {
  const response = await fetch(`${apiUrl}/api/v1/public/appointments`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: await request.text(), cache: 'no-store' });
  return new Response(await response.text(), { status: response.status, headers: { 'Content-Type': response.headers.get('Content-Type') ?? 'application/json' } });
}
