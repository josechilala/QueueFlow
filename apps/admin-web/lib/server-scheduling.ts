import { cookies } from 'next/headers';
import { ACCESS_COOKIE, apiUrl } from './auth';

export type SchedulingSettings = { serviceId: string; attendanceMode: 'QueueOnly' | 'AppointmentOnly' | 'Hybrid'; slotDurationMinutes: number; capacityPerSlot: number; minimumAdvanceMinutes: number; maximumAdvanceDays: number; lateToleranceMinutes: number; cancellationDeadlineMinutes: number; checkInAdvanceMinutes: number; allowCustomerCancellation: boolean; allowCustomerReschedule: boolean; requireConfirmation: boolean; isActive: boolean };
export type ServiceSchedule = { id: string; dayOfWeek: number; startTime: string; endTime: string; isActive: boolean };
export type ScheduleBlock = { id: string; branchId: string; serviceId: string | null; startAt: string; endAt: string; reason: string; blockType: 'Maintenance' | 'Holiday' | 'Manual' };
export type SchedulingData = { status: number; settings?: SchedulingSettings; schedules?: ServiceSchedule[]; blocks?: ScheduleBlock[] };

export async function getSchedulingData(serviceId: string): Promise<SchedulingData> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return { status: 401 };
  const headers = { Authorization: `Bearer ${token}` };
  const [settings, schedules, blocks] = await Promise.all([
    fetch(`${apiUrl}/api/v1/services/${serviceId}/scheduling-settings`, { headers, cache: 'no-store' }),
    fetch(`${apiUrl}/api/v1/services/${serviceId}/schedules`, { headers, cache: 'no-store' }),
    fetch(`${apiUrl}/api/v1/schedule-blocks?serviceId=${serviceId}`, { headers, cache: 'no-store' }),
  ]);
  const failed = [settings, schedules, blocks].find(response => !response.ok);
  if (failed) return { status: failed.status };
  return { status: 200, settings: await settings.json(), schedules: await schedules.json(), blocks: await blocks.json() };
}
