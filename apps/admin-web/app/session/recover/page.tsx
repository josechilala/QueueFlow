import { SessionRecovery } from './session-recovery';
import { safeReturnTo } from '../../../../../packages/session/recovery';

export default async function RecoveryPage({ searchParams }: { searchParams: Promise<{ returnTo?: string }> }) {
  const { returnTo } = await searchParams;
  return <main className="centered"><section className="notice"><SessionRecovery returnTo={safeReturnTo(returnTo)} /></section></main>;
}
