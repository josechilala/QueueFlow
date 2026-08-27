import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';
import type { Counter } from './counters';

async function query<T>(path: string): Promise<{ status: number; data?: T }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await fetch(`${apiUrl}${path}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) return { status: response.status };
  return { status: 200, data: (await response.json()) as T };
}

export function getCounters() { return query<Counter[]>('/api/v1/counters'); }
export function getCounter(id: string) { return query<Counter>(`/api/v1/counters/${encodeURIComponent(id)}`); }
