import { cookies } from 'next/headers'; import { ACCESS_COOKIE, apiUrl } from './auth'; import type { ManagementReport } from './reports';
export async function getManagementReport(from: string, to: string): Promise<{ status: number; data?: ManagementReport }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value; if (!token) return { status: 401 };
  const query = new URLSearchParams({ from: new Date(`${from}T00:00:00Z`).toISOString(), to: new Date(`${to}T23:59:59.999Z`).toISOString() });
  const response = await fetch(`${apiUrl}/api/v1/reports?${query}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  return response.ok ? { status: 200, data: await response.json() as ManagementReport } : { status: response.status };
}
