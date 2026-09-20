import { configuredUrl } from './configured-url';
export const ACCESS_COOKIE = 'queueflow_attendant_access';
export const REFRESH_COOKIE = 'queueflow_attendant_refresh';
export type User = { userId: string; organizationId: string; name: string; email: string; role: 'Owner' | 'Admin' | 'Manager' | 'Attendant' | 'Viewer' };
export type TokenPair = { accessToken: string; refreshToken: string };
export const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');
