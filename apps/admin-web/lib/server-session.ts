import { sessionFetch } from '../../../packages/session/server';
import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl, type AuthenticatedUser } from './auth';

export async function getServerSession(): Promise<{ status: number; user?: AuthenticatedUser }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await sessionFetch(`${apiUrl}/api/v1/auth/me`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) return { status: response.status };
  return { status: 200, user: (await response.json()) as AuthenticatedUser };
}
