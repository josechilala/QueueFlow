import type { AppointmentStatus } from './server-appointments';

export const appointmentStatusLabels: Record<AppointmentStatus, string> = {
  Scheduled: 'Registrado',
  Confirmed: 'Confirmado',
  CheckedIn: 'Chegada confirmada',
  Completed: 'Concluído',
  Cancelled: 'Cancelado',
  NoShow: 'Não compareceu',
  Rescheduled: 'Reagendado',
};
