import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';

export type OnboardingProgress = { completed: boolean; branchReady: boolean; servicesReady: boolean; operationReady: boolean; nextStep: string };
export async function getOnboardingProgress(): Promise<OnboardingProgress> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  const response = await fetch(`${apiUrl}/api/v1/onboarding`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) throw new Error('Não foi possível consultar o progresso da configuração.');
  return response.json() as Promise<OnboardingProgress>;
}
