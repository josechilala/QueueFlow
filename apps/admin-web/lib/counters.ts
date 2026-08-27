export type Counter = { id: string; branchId: string; name: string; isActive: boolean };

export async function counterResponseMessage(response: Response): Promise<string> {
  const problem = await response.json().catch(() => null) as { detail?: string; message?: string } | null;
  return problem?.detail ?? problem?.message ?? 'Não foi possível concluir a operação.';
}
