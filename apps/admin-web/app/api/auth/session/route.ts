import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { ACCESS_COOKIE, apiUrl } from '../../../../lib/auth';
import { clearAuthCookies } from '../../../../lib/auth-cookies';

export async function GET() {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ message: 'Não autenticado.' }, { status: 401 });
  const apiResponse = await fetch(`${apiUrl}/api/v1/auth/me`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (apiResponse.ok) return NextResponse.json(await apiResponse.json());
  if (apiResponse.status === 403) return NextResponse.json({ message: 'Permissão insuficiente.' }, { status: 403 });
  const response = NextResponse.json({ message: 'Sessão inválida.' }, { status: 401 }); clearAuthCookies(response); return response;
}
