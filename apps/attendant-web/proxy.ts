import { NextRequest, NextResponse } from 'next/server';
import { ACCESS_COOKIE } from './lib/auth';
import { publicUrl } from './lib/public-url';
export function proxy(request: NextRequest) { return request.cookies.has(ACCESS_COOKIE) ? NextResponse.next() : NextResponse.redirect(publicUrl(request, '/login')); }
export const config = { matcher: ['/workstation/:path*'] };
