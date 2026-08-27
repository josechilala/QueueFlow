import { forwardAuthenticatedJson } from '../../../../../lib/server-api-proxy';
export async function PUT(request: Request, { params }: { params: Promise<{ id: string }> }) { return forwardAuthenticatedJson(request, `/api/v1/services/${(await params).id}/scheduling-settings`, 'PUT'); }
