import { tenantSession } from '../../../../lib/session';
import { correlationId } from '../../../../../../packages/session/correlation';
export function GET(request?: Request) { return tenantSession.forward('/api/v1/auth/me', { headers: { 'X-Correlation-ID': correlationId(request?.headers.get('X-Correlation-ID')) } }); }
