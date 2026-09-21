export function formatAppointmentDateTime(scheduledStart: string, timeZone: string): string {
  return new Intl.DateTimeFormat('pt-BR', {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(scheduledStart));
}
