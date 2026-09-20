import { redirect } from 'next/navigation';
import { PlatformShell } from '../../../components/platform-shell';
import { getPlatformSession } from '../../../lib/server-platform';

export default async function PlatformLayout({ children }: { children: React.ReactNode }) {
  const session = await getPlatformSession();
  if (session.status === 401) redirect('/api/platform/auth/refresh');
  if (session.status === 403) return <main><h1>Acesso não permitido</h1></main>;
  if (session.status !== 200 || !session.data) throw new Error('Não foi possível validar a sessão. Tente novamente.');
  return <PlatformShell user={session.data}>{children}</PlatformShell>;
}
