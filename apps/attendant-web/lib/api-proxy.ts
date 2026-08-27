import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { ACCESS_COOKIE, apiUrl } from './auth';

export async function post(path: string, request: Request) {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ message: 'Não autenticado.' }, { status: 401 });
  const response = await fetch(`${apiUrl}${path}`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: (await request.text()) || '{}', cache: 'no-store' });
  return new NextResponse(await response.text(), { status: response.status, headers: { 'Content-Type': response.headers.get('Content-Type') ?? 'application/json' } });
}
