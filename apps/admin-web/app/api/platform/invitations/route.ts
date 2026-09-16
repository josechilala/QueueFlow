import { forwardPlatformJson } from '../../../../lib/server-platform-proxy';
export function POST(request: Request) { return forwardPlatformJson(request, '/api/v1/platform/invitations', 'POST'); }
