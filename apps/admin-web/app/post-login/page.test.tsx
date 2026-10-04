import { beforeEach, expect, it, vi } from 'vitest';
import { getOnboardingProgress } from '../../lib/server-onboarding';
import { getServerSession } from '../../lib/server-session';
import PostLoginPage from './page';

vi.mock('../../lib/server-session', () => ({ getServerSession: vi.fn() }));
vi.mock('../../lib/server-onboarding', () => ({ getOnboardingProgress: vi.fn() }));
vi.mock('next/navigation', () => ({ redirect: (path: string) => { throw new Error(`redirect:${path}`); } }));

const sessionMock = vi.mocked(getServerSession);
const onboardingMock = vi.mocked(getOnboardingProgress);

beforeEach(() => vi.clearAllMocks());

it('sends an incomplete Owner to onboarding after authentication', async () => {
  sessionMock.mockResolvedValue({ status: 200, user: { role: 'Owner' } as never });
  onboardingMock.mockResolvedValue({ completed: false, branchReady: true, servicesReady: true, operationReady: false, nextStep: 'operation' });
  await expect(PostLoginPage({ searchParams: Promise.resolve({ returnTo: '/dashboard' }) })).rejects.toThrow('redirect:/onboarding');
});

it('sends a ready Owner to the safe requested destination', async () => {
  sessionMock.mockResolvedValue({ status: 200, user: { role: 'Owner' } as never });
  onboardingMock.mockResolvedValue({ completed: false, branchReady: true, servicesReady: true, operationReady: true, nextStep: 'links' });
  await expect(PostLoginPage({ searchParams: Promise.resolve({ returnTo: '/queues' }) })).rejects.toThrow('redirect:/queues');
});

it('does not query onboarding for a non-Owner admin role', async () => {
  sessionMock.mockResolvedValue({ status: 200, user: { role: 'Admin' } as never });
  await expect(PostLoginPage({ searchParams: Promise.resolve({ returnTo: '/users' }) })).rejects.toThrow('redirect:/users');
  expect(onboardingMock).not.toHaveBeenCalled();
});

it('sanitizes an external return destination', async () => {
  sessionMock.mockResolvedValue({ status: 200, user: { role: 'Admin' } as never });
  await expect(PostLoginPage({ searchParams: Promise.resolve({ returnTo: 'https://evil.example' }) })).rejects.toThrow('redirect:/dashboard');
});
