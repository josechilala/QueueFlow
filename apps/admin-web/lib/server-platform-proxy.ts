import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { apiUrl, PLATFORM_ACCESS_COOKIE } from './platform-auth';

export async function forwardPlatformJson(request: Request, path: string, method: 'POST') {
  const token = (await cookies()).get(PLATFORM_ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ message: 'Não autenticado.' }, { status: 401 });
  const apiResponse = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: await request.text(),
    cache: 'no-store',
  });
  return new NextResponse(apiResponse.status === 204 ? null : await apiResponse.text(), { status: apiResponse.status, headers: { 'Content-Type': apiResponse.headers.get('Content-Type') ?? 'application/json', 'Cache-Control': 'no-store' } });
}
