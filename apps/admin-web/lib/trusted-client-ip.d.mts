import type { IncomingMessage } from 'node:http';
export function createProxyTrust(value?: string): (address: string) => boolean;
export function stampClientIp(request: IncomingMessage, trusted: (address: string) => boolean, key: string): void;
export function clientIpHeaders(headers: Headers): Record<string, string>;
