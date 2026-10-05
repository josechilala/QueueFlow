import { redirect } from 'next/navigation';
import { authFailure } from '../../lib/auth';
import { getOnboardingProgress } from '../../lib/server-onboarding';
import { getServerSession } from '../../lib/server-session';
import { safeReturnTo } from '../../../../packages/session/recovery';

export default async function PostLoginPage({ searchParams }: { searchParams: Promise<{ returnTo?: string }> }) {
  const { returnTo } = await searchParams;
  const destination = safeReturnTo(returnTo);
  const session = await getServerSession();
  const failure = authFailure(session.status);

  if (failure === 'unauthorized') redirect(`/api/auth/refresh?returnTo=${encodeURIComponent(`/post-login?returnTo=${encodeURIComponent(destination)}`)}`);
  if (failure === 'forbidden') redirect('/dashboard');
  if (!session.user) throw new Error('Não foi possível validar a sessão administrativa.');
  if (session.user.role !== 'Owner') redirect(destination);

  const progress = await getOnboardingProgress();
  const ready = progress.completed || (progress.branchReady && progress.servicesReady && progress.operationReady);
  redirect(ready ? destination : '/onboarding');
}
