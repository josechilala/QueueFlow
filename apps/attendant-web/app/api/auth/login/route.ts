import { NextResponse } from 'next/server';
import { apiUrl, type TokenPair } from '../../../../lib/auth';
import { setAuthCookie } from '../../../../lib/auth-cookies';
import { correlationId } from '../../../../../../packages/session/correlation';

export async function POST(request: Request) {
  const requestId = correlationId(request.headers.get('X-Correlation-ID'));
  const body = await request.text();
  const apiResponse = await fetch(`${apiUrl}/api/v1/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': requestId }, body, cache: 'no-store' });
  if (!apiResponse.ok) return NextResponse.json({ message: 'E-mail ou senha inválidos.' }, { status: apiResponse.status, headers: { 'X-Correlation-ID': requestId } });
  const response = NextResponse.json({ authenticated: true }, { headers: { 'X-Correlation-ID': requestId } }); setAuthCookie(response, (await apiResponse.json()) as TokenPair); return response;
}
