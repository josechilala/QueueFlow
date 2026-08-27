import { forwardAuthenticatedDelete } from '../../../../lib/server-api-proxy';
export async function DELETE(_: Request, { params }: { params: Promise<{ id: string }> }) { return forwardAuthenticatedDelete(`/api/v1/schedule-blocks/${(await params).id}`); }
