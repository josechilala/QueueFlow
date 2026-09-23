import { configuredUrl } from './configured-url';

const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260');

// Diagnostic metadata must never include the URL token, request body or upstream body.
export async function proxyAppointment(request: Request, publicToken?: string) {
  const operation = publicToken === undefined ? 'create' : 'reschedule';
  const endpoint = publicToken === undefined
    ? '/api/v1/public/appointments'
    : '/api/v1/public/appointments/{publicToken}/reschedule';
  const path = publicToken === undefined
    ? endpoint
    : `/api/v1/public/appointments/${encodeURIComponent(publicToken)}/reschedule`;
  const correlationId = crypto.randomUUID();
  let stage = 'read_request';
  try {
    const body = await request.text();
    stage = 'api_request';
    const response = await fetch(`${apiUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': correlationId },
      body,
      cache: 'no-store',
    });
    stage = 'read_response';
    const responseBody = await response.text();
    if (response.status >= 500) {
      console.error('appointment_proxy_failure', { operation, endpoint, correlationId, stage: 'api_response', status: response.status });
    }
    const headers = new Headers({
      'Content-Type': response.headers.get('Content-Type') ?? 'application/json',
      'X-Correlation-ID': correlationId,
    });
    const retryAfter = response.headers.get('Retry-After');
    if (retryAfter !== null) headers.set('Retry-After', retryAfter);
    return new Response(responseBody, { status: response.status, headers });
  } catch (error) {
    const exceptionType = error instanceof TypeError ? 'TypeError' : 'Error';
    console.error('appointment_proxy_failure', { operation, endpoint, correlationId, stage, exceptionType });
    throw error;
  }
}
