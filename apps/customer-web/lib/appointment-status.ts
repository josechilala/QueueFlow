export type AppointmentStatus = 'Scheduled' | 'Confirmed' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'NoShow' | 'Rescheduled';

export const appointmentPresentation: Record<AppointmentStatus, { title: string; label: string }> = {
  Scheduled: { title: 'AGENDAMENTO REGISTRADO', label: 'Registrado' },
  Confirmed: { title: 'AGENDAMENTO CONFIRMADO', label: 'Confirmado' },
  CheckedIn: { title: 'CHEGADA CONFIRMADA', label: 'Check-in realizado' },
  Completed: { title: 'ATENDIMENTO CONCLUÍDO', label: 'Concluído' },
  Cancelled: { title: 'AGENDAMENTO CANCELADO', label: 'Cancelado' },
  NoShow: { title: 'NÃO COMPARECEU', label: 'Não compareceu' },
  Rescheduled: { title: 'AGENDAMENTO REAGENDADO', label: 'Reagendado' },
};
