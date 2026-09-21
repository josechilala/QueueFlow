import { randomBytes } from 'node:crypto';
import { cookies } from 'next/headers';
import { NextResponse } from 'next/server';
import { tenantSession } from '../../../../lib/session';
import { getServerSession } from '../../../../lib/server-session';
import { publicUrl } from '../../../../lib/public-url';
import { safeReturnTo } from '../../../../../../packages/session/recovery';

export async function GET(request: Request) {
  const returnTo = safeReturnTo(new URL(request.url).searchParams.get('returnTo'));
  const response = NextResponse.redirect(publicUrl(request, `/session/recover?returnTo=${encodeURIComponent(returnTo)}`),
    { status: 303, headers: { 'Cache-Control': 'no-store' } });
  if (!(await cookies()).has('queueflow_refresh_attempt')) response.cookies.set('queueflow_refresh_attempt', randomBytes(32).toString('base64url'), {
    httpOnly: true, sameSite: 'strict', secure: new URL(request.url).protocol === 'https:' || process.env.NODE_ENV === 'production', path: '/', maxAge: 300,
  });
  return response;
}

export async function POST(request: Request) {
  if (request.headers.get('origin') !== publicUrl(request, '/').origin) {
    return NextResponse.json({ message: 'Origem inválida.' }, { status: 403 });
  }
  // The browser lock is held before this check. Another tab may have already
  // persisted the winning cookies, including when its response body was lost.
  const session = await getServerSession();
  if (session.user) return NextResponse.json({ redirectTo: safeReturnTo(new URL(request.url).searchParams.get('returnTo')) },
    { headers: { 'Cache-Control': 'no-store' } });
  if (session.status !== 401) return NextResponse.json({ message: 'Serviço temporariamente indisponível.' },
    { status: session.status, headers: { 'Cache-Control': 'no-store', 'Retry-After': session.retryAfter ?? (session.status === 429 ? '60' : '5') } });
  return tenantSession.refresh(request);
}
