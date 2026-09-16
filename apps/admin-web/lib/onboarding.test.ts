import { describe, expect, it } from 'vitest';
import { operationConfigured } from './onboarding';
import type { Service } from './services';
import type { Queue } from './queues';

const service = (attendanceMode: Service['attendanceMode']) => ({ id: 'service', isActive: true, attendanceMode } as Service);
const queue = { serviceId: 'service', isActive: true } as Queue;
describe('onboarding operation readiness', () => {
  it('requires an active service', () => expect(operationConfigured([], [], [])).toBe(false));
  it('allows a queue-only operation without schedules', () => expect(operationConfigured([service('QueueOnly')], [queue], [])).toBe(true));
  it('allows appointment-only operation without a queue', () => expect(operationConfigured([service('AppointmentOnly')], [], ['service'])).toBe(true));
  it('requires both a queue and schedule for hybrid operation', () => {
    expect(operationConfigured([service('Hybrid')], [queue], [])).toBe(false);
    expect(operationConfigured([service('Hybrid')], [], ['service'])).toBe(false);
    expect(operationConfigured([service('Hybrid')], [queue], ['service'])).toBe(true);
  });
});
