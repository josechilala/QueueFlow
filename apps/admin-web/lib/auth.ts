import { configuredUrl } from './configured-url';
export const ACCESS_COOKIE = 'queueflow_access';
export const REFRESH_COOKIE = 'queueflow_refresh';

export type AuthenticatedUser = {
  userId: string;
  organizationId: string;
  organizationName?: string | null;
  name: string;
  email: string;
  role: 'Owner' | 'Admin' | 'Manager' | 'Attendant' | 'Viewer';
};

export type TokenPair = { accessToken: string; refreshToken: string };
export const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');

export function authFailure(status: number): 'unauthorized' | 'forbidden' | 'other' {
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  return 'other';
}
