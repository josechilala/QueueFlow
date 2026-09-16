import { publicUrl } from '../../../../../lib/public-url';
import { NextResponse } from 'next/server';
import { clearPlatformAuthCookies } from '../../../../../lib/platform-auth-cookies';
export async function POST(request: Request) { const response = NextResponse.redirect(publicUrl(request, '/platform/login'), 303); return clearPlatformAuthCookies(response); }
