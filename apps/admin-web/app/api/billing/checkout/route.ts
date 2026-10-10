import { forwardAuthenticatedJson } from '../../../../lib/server-api-proxy';
export function POST(request: Request) {
  if (request.headers.get('content-type')?.split(';')[0].trim() !== 'application/json' ||
      request.headers.get('sec-fetch-site') === 'cross-site') return new Response(null, { status: 403 });
  return forwardAuthenticatedJson(request, '/api/v1/billing/checkout', 'POST');
}
