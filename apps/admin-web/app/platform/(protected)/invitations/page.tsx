import { getPlatformInvitations } from '../../../../lib/server-platform';
import { InvitationManager } from './invitation-manager';

export default async function InvitationsPage() {
  const result = await getPlatformInvitations();
  return <><div className="page-heading"><h1>Convites de organização</h1><p className="muted">Crie o acesso seguro para um novo owner concluir a ativação.</p></div><InvitationManager initial={result.data ?? []} /></>;
}
