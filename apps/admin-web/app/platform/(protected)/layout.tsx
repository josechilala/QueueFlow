import { redirect } from 'next/navigation';
import { PlatformShell } from '../../../components/platform-shell';
import { getPlatformSession } from '../../../lib/server-platform';

export default async function PlatformLayout({ children }: { children: React.ReactNode }) {
  const session = await getPlatformSession();
  if (session.status === 401) redirect('/api/platform/auth/refresh');
  if (session.status !== 200 || !session.data) redirect('/platform/login');
  return <PlatformShell user={session.data}>{children}</PlatformShell>;
}
