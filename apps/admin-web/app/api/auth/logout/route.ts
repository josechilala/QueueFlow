import { NextResponse } from 'next/server';
import { clearAuthCookies } from '../../../../lib/auth-cookies';
import { publicUrl } from '../../../../lib/public-url';

function loggedOut(request: Request) { const response = NextResponse.redirect(publicUrl(request, '/login'), 303); clearAuthCookies(response); return response; }
export async function GET(request: Request) { return loggedOut(request); }
export async function POST(request: Request) { return loggedOut(request); }
