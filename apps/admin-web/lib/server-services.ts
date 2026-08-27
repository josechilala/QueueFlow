import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';
import type { Service } from './services';

export async function getServices(): Promise<{ status: number; data?: Service[] }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await fetch(`${apiUrl}/api/v1/services`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) return { status: response.status };
  return { status: 200, data: (await response.json()) as Service[] };
}
