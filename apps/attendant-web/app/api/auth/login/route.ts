import { NextResponse } from 'next/server';
import { apiUrl, type TokenPair } from '../../../../lib/auth';
import { setAuthCookie } from '../../../../lib/auth-cookies';

export async function POST(request: Request) {
  const body = await request.text();
  const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, cache: 'no-store' });
  if (!apiResponse.ok) return NextResponse.json({ message: 'E-mail ou senha inválidos.' }, { status: apiResponse.status });
  const response = NextResponse.json({ authenticated: true }); setAuthCookie(response, (await apiResponse.json()) as TokenPair); return response;
}
