import Link from 'next/link';
import type { ReactNode } from 'react';
import type { AuthenticatedUser } from '../lib/auth';
import { LogoutButton } from '../app/dashboard/logout-button';

export function AdminShell({ user, children }: { user: AuthenticatedUser; children: ReactNode }) {
  const canManage = ['Owner', 'Admin', 'Manager'].includes(user.role); const canReadReports = [...(canManage ? [user.role] : []), 'Viewer'].includes(user.role);
  return <main className="app-shell"><aside><h1>QueueFlow</h1><nav>
    <Link href="/dashboard">Visão geral</Link><Link href="/branches">Unidades</Link><Link href="/services">Serviços</Link><Link href="/counters">Guichês</Link><Link href="/queues">Filas</Link><Link href="/appointments">Agendamentos</Link>
    {canManage ? <Link href="/users">Usuários</Link> : <span>Usuários</span>}{canReadReports ? <Link href="/reports">Relatórios</Link> : <span>Relatórios</span>}{['Owner', 'Admin'].includes(user.role) ? <Link href="/audit">Auditoria</Link> : <span>Auditoria</span>}
  </nav></aside><section className="dashboard"><header><div><small>SESSÃO AUTENTICADA</small><h2>Olá, {user.name}</h2><p className="muted">{user.email}</p><p className="muted">{user.organizationName ? `${user.organizationName} — ${user.role}` : user.role}</p></div><LogoutButton /></header>{children}</section></main>;
}
