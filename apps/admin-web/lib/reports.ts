export type ManagementReport = {
  from: string; to: string; totalTickets: number; completed: number; noShows: number; cancelled: number; averageWaitMinutes: number; averageServiceMinutes: number;
  appointments: { total: number; scheduled: number; confirmed: number; checkedIn: number; completed: number; cancelled: number; noShows: number; rescheduled: number; checkInRatePercent: number };
  byDay: { date: string; volume: number }[]; byService: { id: string; name: string; volume: number }[]; byBranch: { id: string; name: string; volume: number }[]; byHour: { hour: number; volume: number }[];
};
