import type { Service } from './services';
import type { Queue } from './queues';

export function operationConfigured(services: Service[], queues: Queue[], scheduledServiceIds: string[]) {
  const active = services.filter(service => service.isActive);
  return active.length > 0 && active.every(service =>
    (service.attendanceMode === 'AppointmentOnly' || queues.some(queue => queue.isActive && queue.serviceId === service.id)) &&
    (service.attendanceMode === 'QueueOnly' || scheduledServiceIds.includes(service.id)));
}
