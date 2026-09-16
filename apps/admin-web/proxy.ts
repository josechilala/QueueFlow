import { NextRequest, NextResponse } from 'next/server';
import { ACCESS_COOKIE } from './lib/auth';
import { PLATFORM_ACCESS_COOKIE, PLATFORM_REFRESH_COOKIE } from './lib/platform-auth';
import { publicUrl } from './lib/public-url';

export function proxy(request: NextRequest) {
  if (request.nextUrl.pathname.startsWith('/platform')) {
    if (request.nextUrl.pathname === '/platform/login') return NextResponse.next();
    return request.cookies.has(PLATFORM_ACCESS_COOKIE) ? NextResponse.next() : NextResponse.redirect(publicUrl(request, request.cookies.has(PLATFORM_REFRESH_COOKIE) ? '/api/platform/auth/refresh' : '/platform/login'));
  }
  return request.cookies.has(ACCESS_COOKIE) ? NextResponse.next() : NextResponse.redirect(publicUrl(request, '/login'));
}

export const config = { matcher: ['/dashboard/:path*', '/branches/:path*', '/platform/:path*', '/onboarding/:path*'] };
