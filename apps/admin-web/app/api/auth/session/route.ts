import { tenantSession } from '../../../../lib/session';
export function GET() { return tenantSession.forward('/api/v1/auth/me'); }
