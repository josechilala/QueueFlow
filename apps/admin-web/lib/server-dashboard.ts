import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';
import type { DashboardSummary } from './dashboard';

export async function getDashboardSummary(): Promise<{ status: number; summary?: DashboardSummary }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };

  const response = await fetch(`${apiUrl}/api/v1/dashboard`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: 'no-store',
  });

  if (!response.ok) return { status: response.status };
  return { status: 200, summary: (await response.json()) as DashboardSummary };
}
