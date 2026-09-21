import { expect, it } from 'vitest';
import { formatAppointmentDateTime } from './appointment-time';

it('shows the confirmed customer time in the appointment timezone', () => {
  expect(formatAppointmentDateTime('2026-09-22T12:30:00+00:00', 'America/Sao_Paulo'))
    .toBe('22/09/2026, 09:30:00');
});

it('converts the date as well as the hour when crossing midnight', () => {
  expect(formatAppointmentDateTime('2026-09-22T01:30:00Z', 'America/Sao_Paulo'))
    .toBe('21/09/2026, 22:30:00');
});

it('uses each appointment timezone instead of a fixed Brazil offset', () => {
  const start = '2026-09-22T12:30:00Z';
  expect(formatAppointmentDateTime(start, 'UTC')).toBe('22/09/2026, 12:30:00');
  expect(formatAppointmentDateTime(start, 'Asia/Kolkata')).toBe('22/09/2026, 18:00:00');
});

it('applies daylight saving rules from the saved timezone', () => {
  expect(formatAppointmentDateTime('2026-07-22T12:30:00Z', 'America/New_York'))
    .toBe('22/07/2026, 08:30:00');
  expect(formatAppointmentDateTime('2026-01-22T12:30:00Z', 'America/New_York'))
    .toBe('22/01/2026, 07:30:00');
});
