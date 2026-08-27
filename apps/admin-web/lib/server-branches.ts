import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';
import type { Branch } from './branches';

async function query<T>(path: string): Promise<{ status: number; data?: T }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await fetch(`${apiUrl}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: 'no-store',
  });
  if (!response.ok) return { status: response.status };
  return { status: 200, data: (await response.json()) as T };
}

export function getBranches() {
  return query<Branch[]>('/api/v1/branches');
}

export function getBranch(id: string) {
  return query<Branch>(`/api/v1/branches/${encodeURIComponent(id)}`);
}
