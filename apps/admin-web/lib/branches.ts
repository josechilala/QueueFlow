export type Branch = {
  id: string;
  publicId: string;
  name: string;
  address: string | null;
  timeZone: string;
  isActive: boolean;
};

export type BranchInput = Pick<Branch, 'name' | 'address' | 'timeZone'>;

export async function responseMessage(response: Response): Promise<string> {
  const problem = await response.json().catch(() => null) as { detail?: string; message?: string } | null;
  return problem?.detail ?? problem?.message ?? 'Não foi possível concluir a operação.';
}
