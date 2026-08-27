import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';

export type AppointmentStatus = 'Scheduled' | 'Confirmed' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'NoShow' | 'Rescheduled';
export type AppointmentListItem = { id: string; customerName: string; customerPhone: string | null; customerEmail: string | null; status: AppointmentStatus; scheduledStart: string; scheduledEnd: string; branchId: string; branchName: string; serviceId: string; serviceName: string };
export type AppointmentDetails = AppointmentListItem & { timeZone: string; confirmationCode: string; notes: string | null; queueTicketId: string | null; history: { previousStatus: AppointmentStatus; newStatus: AppointmentStatus; reason: string | null; createdAt: string }[] };

async function authenticatedGet<T>(path: string): Promise<{ status: number; data?: T }> { const token = (await cookies()).get(ACCESS_COOKIE)?.value; if (!token) return { status: 401 }; const response = await fetch(`${apiUrl}${path}`, { headers: { Authorization: `Bearer ${token}` }, cache: 'no-store' }); return response.ok ? { status: 200, data: await response.json() } : { status: response.status }; }
export function getAppointments(query: string) { return authenticatedGet<AppointmentListItem[]>(`/api/v1/appointments${query ? `?${query}` : ''}`); }
export function getAppointment(id: string) { return authenticatedGet<AppointmentDetails>(`/api/v1/appointments/${id}`); }
