import type { AuthenticatedUser } from './auth';
export type UserRole = AuthenticatedUser['role'];
export type ManagedUser = { id: string; name: string; email: string; role: UserRole; isActive: boolean; branchIds: string[] };
export const roleLabels: Record<UserRole, string> = { Owner: 'Proprietário', Admin: 'Administrador', Manager: 'Gerente', Attendant: 'Atendente', Viewer: 'Visualizador' };
export function assignableRoles(actor: UserRole): UserRole[] {
  if (actor === 'Owner') return ['Owner', 'Admin', 'Manager', 'Attendant', 'Viewer'];
  if (actor === 'Admin') return ['Admin', 'Manager', 'Attendant', 'Viewer'];
  if (actor === 'Manager') return ['Attendant', 'Viewer'];
  return [];
}
