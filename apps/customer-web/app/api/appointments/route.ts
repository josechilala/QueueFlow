import { proxyAppointment } from '../../../lib/appointment-proxy';
export async function POST(request: Request) {
  return proxyAppointment(request);
}
