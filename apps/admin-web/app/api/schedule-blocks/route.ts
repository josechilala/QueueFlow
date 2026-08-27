import { forwardAuthenticatedJson } from '../../../lib/server-api-proxy';
export async function POST(request: Request) { return forwardAuthenticatedJson(request, '/api/v1/schedule-blocks', 'POST'); }
