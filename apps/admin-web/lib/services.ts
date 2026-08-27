export type Service = {
  id: string;
  branchId: string;
  publicId: string;
  name: string;
  description: string | null;
  prefix: string;
  averageDurationMinutes: number;
  isActive: boolean;
  attendanceMode: 'QueueOnly' | 'AppointmentOnly' | 'Hybrid';
};

export async function serviceResponseMessage(response: Response): Promise<string> {
  const problem = await response.json().catch(() => null) as { detail?: string; message?: string } | null;
  return problem?.detail ?? problem?.message ?? 'Não foi possível concluir a operação.';
}
