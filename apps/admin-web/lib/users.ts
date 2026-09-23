import type { AuthenticatedUser } from './auth';
export type UserRole = AuthenticatedUser['role'];
export type ManagedUser = { id: string; name: string; email: string; role: UserRole; isActive: boolean; branchIds: string[] };
export const roleLabels: Record<UserRole, string> = { Owner: 'Proprietário', Admin: 'Administrador', Manager: 'Gerente', Attendant: 'Atendente', Viewer: 'Visualizador' };
export function assignableRoles(actor: UserRole): UserRole[] {
  if (actor === 'Owner' || actor === 'Admin') return ['Admin', 'Manager', 'Attendant'];
  if (actor === 'Manager') return ['Attendant'];
  return [];
}
export function canManageUser(actor: UserRole, target: UserRole): boolean {
  return assignableRoles(actor).includes(target) || (target === 'Viewer' && assignableRoles(actor).length > 0);
}
