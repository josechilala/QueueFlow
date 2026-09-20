import { sessionFetch } from '../../../packages/session/server';
import { cookies } from 'next/headers';
import { apiUrl, PLATFORM_ACCESS_COOKIE, type PlatformUser } from './platform-auth';

export type PlatformDashboard = { totalOrganizations: number; activeOrganizations: number; trials: number; activeSubscriptions: number; pendingInvitations: number; expiredInvitations: number };
export type PlatformInvitation = { id: string; email: string; responsibleName: string | null; organizationName: string | null; plan: string | null; status: 'Pending' | 'Verified' | 'Used' | 'Expired' | 'Revoked'; createdAt: string; expiresAt: string; verifiedAt: string | null; usedAt: string | null; revokedAt: string | null; activatedOrganizationId: string | null };
export type PlatformOrganization = { id: string; name: string; slug: string; ownerName: string | null; ownerEmail: string | null; createdAt: string; isActive: boolean; plan: string | null; subscriptionStatus: string | null; trialEndsAt: string | null; branchCount: number; userCount: number };
export type PlatformSubscription = { organizationId: string; organizationName: string; plan: string; status: string; trialEndsAt: string };
export type PlatformAudit = { id: string; platformUserId: string | null; action: string; resourceType: string; resourceId: string | null; createdAt: string };
async function get<T>(path: string): Promise<{ status: number; data?: T }> { const token = (await cookies()).get(PLATFORM_ACCESS_COOKIE)?.value; if (!token) return { status: 401 }; const response = await sessionFetch(`${apiUrl}${path}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' }); return response.ok ? { status: 200, data: await response.json() as T } : { status: response.status }; }
export const getPlatformSession = () => get<PlatformUser>('/api/v1/platform/auth/me');
export const getPlatformDashboard = () => get<PlatformDashboard>('/api/v1/platform/dashboard');
export const getPlatformInvitations = () => get<PlatformInvitation[]>('/api/v1/platform/invitations');
export const getPlatformOrganizations = () => get<PlatformOrganization[]>('/api/v1/platform/organizations');
export const getPlatformSubscriptions = () => get<PlatformSubscription[]>('/api/v1/platform/subscriptions');
export const getPlatformAudit = () => get<PlatformAudit[]>('/api/v1/platform/audit');
