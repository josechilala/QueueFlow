export type DashboardSummary = {
  organizationSlug: string;
  activeQueues: number;
  inService: number;
  appointmentsToday: number;
  upcomingAppointments: number;
  activeBranches: number;
  queues: QueueDashboardItem[];
  waiting: number;
  completedToday: number;
  averageWaitMinutes: number;
  generatedAt: string;
  queuesInProgress: QueueDashboardItem[];
};

export type QueueDashboardItem = {
    id: string;
    name: string;
    branchName: string;
    serviceName: string;
    status: 'Draft' | 'Open' | 'Paused' | 'Closed';
    waiting: number;
    activeAttendants: number;
    estimatedWaitMinutes: number;
};

export function hasOperationalData(summary: DashboardSummary): boolean {
  return summary.activeQueues > 0 || summary.waiting > 0 || summary.completedToday > 0 || summary.queues.length > 0 || summary.inService > 0 || summary.appointmentsToday > 0 || summary.upcomingAppointments > 0 || summary.activeBranches > 0;
}
