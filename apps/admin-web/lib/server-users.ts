import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';
import type { ManagedUser } from './users';
async function query<T>(path: string): Promise<{ status: number; data?: T }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await fetch(`${apiUrl}${path}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) return { status: response.status };
  return { status: 200, data: await response.json() as T };
}
export function getUsers() { return query<ManagedUser[]>('/api/v1/users'); }
export function getUser(id: string) { return query<ManagedUser>(`/api/v1/users/${encodeURIComponent(id)}`); }
