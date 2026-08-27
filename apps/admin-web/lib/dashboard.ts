export type DashboardSummary = {
  activeQueues: number;
  waiting: number;
  completedToday: number;
  averageWaitMinutes: number;
  generatedAt: string;
  queuesInProgress: {
    id: string;
    name: string;
    branchName: string;
    serviceName: string;
    status: 'Open' | 'Paused';
    waiting: number;
    activeAttendants: number;
    estimatedWaitMinutes: number;
  }[];
};

export function hasOperationalData(summary: DashboardSummary): boolean {
  return summary.activeQueues > 0 || summary.waiting > 0 || summary.completedToday > 0 || summary.queuesInProgress.length > 0;
}
