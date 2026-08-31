import { describe, expect, it } from 'vitest';
import { appointmentOriginLabel, buildPublicAppointmentUrl } from './appointment-trace';

describe('appointment traceability', () => {
  it('identifica reservas criadas pelo cliente', () => {
    expect(appointmentOriginLabel.PublicPortal).toBe('Cliente pelo portal público');
  });

  it('constrói o link individual usando a base configurada', () => {
    expect(buildPublicAppointmentUrl('https://agenda.queueflow.com/', 'token-123')).toBe('https://agenda.queueflow.com/meu-agendamento/token-123');
  });

  it('não produz link quebrado sem base ou token', () => {
    expect(buildPublicAppointmentUrl('', 'token')).toBeNull();
    expect(buildPublicAppointmentUrl('https://agenda.queueflow.com', '')).toBeNull();
  });
});
