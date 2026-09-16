import { apiUrl, type TokenPair } from './auth';

export const PLATFORM_ACCESS_COOKIE = 'queueflow_platform_access';
export const PLATFORM_REFRESH_COOKIE = 'queueflow_platform_refresh';
export type PlatformUser = { userId: string; name: string; email: string };
export { apiUrl, type TokenPair };
