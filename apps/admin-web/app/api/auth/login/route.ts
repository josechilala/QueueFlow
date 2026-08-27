import { NextResponse } from 'next/server';
import { apiUrl, type AuthenticatedUser, type TokenPair } from '../../../../lib/auth';
import { setAuthCookies } from '../../../../lib/auth-cookies';

export async function POST(request: Request) {
  const body = (await request.json()) as { email?: string; password?: string };
  if (!body.email || !body.password) return NextResponse.json({ message: 'E-mail e senha são obrigatórios.' }, { status: 400 });
  const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), cache: 'no-store' });
  if (!apiResponse.ok) return NextResponse.json({ message: apiResponse.status === 401 ? 'E-mail ou senha inválidos.' : 'Não foi possível entrar agora.' }, { status: apiResponse.status });
  const tokens = (await apiResponse.json()) as TokenPair;
  const sessionResponse = await fetch(`${apiUrl}/api/v1/auth/me`, { headers: { Authorization: `Bearer ${tokens.accessToken}` }, cache: 'no-store' });
  if (!sessionResponse.ok) return NextResponse.json({ message: 'Não foi possível carregar o perfil do usuário.' }, { status: 502 });
  const session = (await sessionResponse.json()) as AuthenticatedUser;
  const response = NextResponse.json({ authenticated: true, role: session.role });
  setAuthCookies(response, tokens);
  if (session.role === 'Attendant') {
    response.cookies.set('queueflow_attendant_access', tokens.accessToken, {
      httpOnly: true,
      sameSite: 'lax',
      secure: process.env.QUEUEFLOW_SECURE_COOKIES === 'true',
      path: '/',
      maxAge: 15 * 60
    });
  }
  return response;
}
