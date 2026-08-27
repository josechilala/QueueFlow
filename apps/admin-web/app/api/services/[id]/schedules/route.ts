import { forwardAuthenticatedJson } from '../../../../../lib/server-api-proxy';
export async function POST(request: Request, { params }: { params: Promise<{ id: string }> }) { return forwardAuthenticatedJson(request, `/api/v1/services/${(await params).id}/schedules`, 'POST'); }
