import { NextRequest, NextResponse } from 'next/server';
import { ACCESS_COOKIE, REFRESH_COOKIE } from './lib/auth';
import { publicUrl } from './lib/public-url';
export function proxy(request: NextRequest) {
  const returnTo = encodeURIComponent(request.nextUrl.pathname + request.nextUrl.search);
  return request.cookies.has(ACCESS_COOKIE) ? NextResponse.next() : NextResponse.redirect(publicUrl(request,
    request.cookies.has(REFRESH_COOKIE) ? `/api/auth/refresh?returnTo=${returnTo}` : '/login'));
}
export const config = { matcher: ['/workstation/:path*'] };
