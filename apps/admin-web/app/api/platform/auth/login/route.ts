import { NextResponse } from 'next/server';
import { apiUrl, type TokenPair } from '../../../../../lib/platform-auth';
import { setPlatformAuthCookies } from '../../../../../lib/platform-auth-cookies';

export async function POST(request: Request) { const body = await request.json(); const response = await fetch(`${apiUrl}/api/v1/platform/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), cache: 'no-store' }); if (!response.ok) return NextResponse.json({ message: 'E-mail ou senha inválidos.' }, { status: response.status }); const result = NextResponse.json({ authenticated: true }); setPlatformAuthCookies(result, await response.json() as TokenPair); return result; }
