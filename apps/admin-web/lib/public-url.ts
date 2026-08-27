export function publicUrl(request: Request, path: string) {
  const configuredOrigin = process.env.QUEUEFLOW_PUBLIC_URL;
  if (configuredOrigin) return new URL(path, configuredOrigin);

  const forwardedHost = request.headers.get('x-forwarded-host');
  const host = forwardedHost ?? request.headers.get('host');
  const protocol = request.headers.get('x-forwarded-proto') ?? new URL(request.url).protocol.replace(':', '');
  return host ? new URL(path, `${protocol}://${host}`) : new URL(path, request.url);
}
