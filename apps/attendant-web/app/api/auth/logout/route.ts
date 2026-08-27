import { NextResponse } from 'next/server';
import { clearAuthCookie } from '../../../../lib/auth-cookies';
import { publicUrl } from '../../../../lib/public-url';
export function POST(request: Request) { const response = NextResponse.redirect(publicUrl(request, '/login'), 303); clearAuthCookie(response); return response; }
