import { forwardAuthenticatedDelete } from '../../../../../../lib/server-api-proxy';
export async function DELETE(_: Request, { params }: { params: Promise<{ id: string; scheduleId: string }> }) { const value = await params; return forwardAuthenticatedDelete(`/api/v1/services/${value.id}/schedules/${value.scheduleId}`); }
