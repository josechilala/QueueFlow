// Civil dates stay as YYYY-MM-DD strings; only API instants are parsed as Date.
export function localSlotDate(instant: string, timeZone: string): string | null {
  if (!/(?:Z|[+-]\d{2}:\d{2})$/i.test(instant)) return null;
  const value = new Date(instant);
  if (!Number.isFinite(value.getTime())) return null;
  try {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(value);
    const part = (type: string) => parts.find(item => item.type === type)!.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
  } catch { return null; }
}

export function tomorrowInTimeZone(now: Date, timeZone: string): string {
  const today = localSlotDate(now.toISOString(), timeZone);
  if (!today) throw new Error('Fuso horário da unidade inválido.');
  const [year, month, day] = today.split('-').map(Number);
  // UTC is used only for calendar arithmetic, after finding the branch's civil day.
  return new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10);
}
