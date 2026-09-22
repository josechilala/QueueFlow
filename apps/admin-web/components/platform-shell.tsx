import Link from 'next/link';
import type { ReactNode } from 'react';
import type { PlatformUser } from '../lib/platform-auth';

export function PlatformShell({ user, children }: { user: PlatformUser; children: ReactNode }) {
  return <main className="app-shell"><aside><h1>QueueFlow</h1><nav><Link href="/platform">Plataforma</Link><Link href="/platform/organizations">Empresas</Link><Link href="/platform/trial-requests">Solicitações de teste</Link><Link href="/platform/invitations">Convites</Link><Link href="/platform/subscriptions">Assinaturas</Link><Link href="/platform/audit">Auditoria</Link></nav></aside><section className="dashboard"><header><div><small>ADMINISTRAÇÃO DA PLATAFORMA</small><h2>Olá, {user.name}</h2><p className="muted">{user.email}</p></div><form action="/api/platform/auth/logout" method="post"><button className="secondary">Sair</button></form></header>{children}</section></main>;
}
