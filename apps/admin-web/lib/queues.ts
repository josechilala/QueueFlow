export type QueueStatus = 'Draft' | 'Open' | 'Paused' | 'Closed';
export type Queue = { id: string; branchId: string; serviceId: string; name: string; publicId: string; status: QueueStatus; capacity: number | null; isActive: boolean };

export const queueStatusLabel: Record<QueueStatus, string> = { Draft: 'Rascunho', Open: 'Aberta', Paused: 'Pausada', Closed: 'Fechada' };
export async function queueResponseMessage(response: Response): Promise<string> {
  const problem = await response.json().catch(() => null) as { detail?: string; message?: string } | null;
  return problem?.detail ?? problem?.message ?? 'Não foi possível concluir a operação.';
}
