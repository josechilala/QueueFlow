import { forwardAuthenticatedJson } from '../../../lib/server-api-proxy';

export function POST(request: Request) {
  return forwardAuthenticatedJson(request, '/api/v1/services', 'POST');
}
