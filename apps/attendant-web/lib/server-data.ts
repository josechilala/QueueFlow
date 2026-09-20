import { sessionFetch } from '../../../packages/session/server';
import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl, type User } from './auth';

export type Branch = { id: string; name: string };
export type Queue = { id: string; publicId: string; branchId: string; name: string; status: 'Draft' | 'Open' | 'Paused' | 'Closed'; waitingCount: number };
export type Counter = { id: string; branchId: string; name: string };
export type CurrentTicket = { id: string; queueId: string; counterId: string | null; ticketNumber: string; status: 'Called' | 'InService'; counterName: string | null };
export type OperationContext = { servicePublicIds?: string[]; branches: Branch[]; queues: Queue[]; counters: Counter[]; currentTicket: CurrentTicket | null };
export type OperationalAppointmentStatus = 'AwaitingConfirmation' | 'AwaitingArrival' | 'Waiting' | 'Called' | 'InService' | 'Completed' | 'NoShow' | 'Cancelled' | 'Rescheduled';
export type OperationalAppointment = {
  id: string;
  branchId: string;
  branchName: string;
  customerName: string;
  serviceName: string;
  appointmentStatus: 'Scheduled' | 'Confirmed' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'NoShow' | 'Rescheduled';
  ticketStatus: 'Waiting' | 'Called' | 'InService' | 'Completed' | 'Cancelled' | 'NoShow' | null;
  ticketNumber: string | null;
  scheduledStart: string;
  scheduledEnd: string;
  scheduledLocalTime: string;
  checkedInAt: string | null;
  queueTicketId: string | null;
  operationalStatus: OperationalAppointmentStatus;
  arrivalConfirmed: boolean;
  canConfirmArrival: boolean;
  checkInAvailableAt: string | null;
  checkInClosesAt: string | null;
  delayMinutes: number | null;
};

async function query<T>(path: string): Promise<{ status: number; data?: T }> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const response = await sessionFetch(`${apiUrl}${path}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' });
  if (!response.ok) return { status: response.status };
  return { status: 200, data: (await response.json()) as T };
}
export function getSession() { return query<User>('/api/v1/auth/me'); }
export function getOperationContext() { return query<OperationContext>('/api/v1/operations/context'); }
export function getTodayAppointments() { return query<OperationalAppointment[]>('/api/v1/operations/appointments/today'); }
