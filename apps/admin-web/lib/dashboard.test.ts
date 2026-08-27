import { describe, expect, it } from 'vitest';
import { hasOperationalData, type DashboardSummary } from './dashboard';

const emptySummary: DashboardSummary = {
  activeQueues: 0,
  waiting: 0,
  completedToday: 0,
  averageWaitMinutes: 0,
  generatedAt: '2026-08-19T12:00:00Z',
  queuesInProgress: [],
};

describe('hasOperationalData', () => {
  it('identifies an empty dashboard', () => {
    expect(hasOperationalData(emptySummary)).toBe(false);
  });

  it('identifies a dashboard backed by operational data', () => {
    expect(hasOperationalData({ ...emptySummary, waiting: 1 })).toBe(true);
  });
});
