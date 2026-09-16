import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { ACCESS_COOKIE, apiUrl } from './auth';
import { clearAuthCookies } from './auth-cookies';

export async function forwardAuthenticatedJson(request: Request, path: string, method: 'POST' | 'PUT' | 'PATCH') {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ message: 'Não autenticado.' }, { status: 401 });

  const apiResponse = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: await request.text(),
    cache: 'no-store',
  });
  const response = new NextResponse(apiResponse.status === 204 ? null : await apiResponse.text(), {
    status: apiResponse.status,
    headers: { 'Content-Type': apiResponse.headers.get('Content-Type') ?? 'application/json' },
  });
  if (apiResponse.status === 401) clearAuthCookies(response);
  return response;
}

export async function forwardAuthenticatedDelete(path: string) {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ message: 'Não autenticado.' }, { status: 401 });
  const apiResponse = await fetch(`${apiUrl}${path}`, { method: 'DELETE', headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  return new NextResponse(apiResponse.status === 204 ? null : await apiResponse.text(), { status: apiResponse.status, headers: { 'Content-Type': apiResponse.headers.get('Content-Type') ?? 'application/json' } });
}
