// @vitest-environment jsdom
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { assignableRoles, roleLabels, type ManagedUser, type UserRole } from '../../lib/users';
import { UserCreateForm } from './user-create-form';
import { UserEditForm } from './[id]/user-edit-form';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn(), push: vi.fn() }) }));
const user: ManagedUser = { id: 'user', name: 'Legacy user', email: 'legacy@example.test', role: 'Viewer', isActive: true, branchIds: [] };
function documentFor(html: string) { return new DOMParser().parseFromString(html, 'text/html'); }

describe('common user role forms', () => {
  it.each(['Owner', 'Admin', 'Manager'] as UserRole[])('offers only permitted common roles to %s in creation and editing', actorRole => {
    const expected = actorRole === 'Manager' ? ['Attendant'] : ['Admin', 'Manager', 'Attendant'];
    for (const html of [renderToStaticMarkup(<UserCreateForm actorRole={actorRole} branches={[]} />),
      renderToStaticMarkup(<UserEditForm user={{ ...user, role: 'Attendant' }} actorRole={actorRole} branches={[]} />)]) {
      const options = Array.from(documentFor(html).querySelectorAll('select option')).filter(option => option.getAttribute('value') !== '');
      expect(options.map(option => option.getAttribute('value'))).toEqual(expected);
      expect(options.map(option => option.textContent)).toEqual(expected.map(role => roleLabels[role as UserRole]));
    }
  });

  it('reads the legacy role without offering it for assignment or silently converting it', () => {
    const document = documentFor(renderToStaticMarkup(<UserEditForm user={user} actorRole="Owner" branches={[]} />));
    expect(document.body.textContent).toContain('Perfil atual: Visualizador');
    expect(document.querySelector('select')?.value).toBe('');
    expect(document.querySelector('option[value="Viewer"]')).toBeNull();
    expect(document.querySelector('button')?.disabled).toBe(true);
  });

  it('keeps ownership outside common editing', () => {
    const document = documentFor(renderToStaticMarkup(<UserEditForm user={{ ...user, role: 'Owner' }} actorRole="Owner" branches={[]} />));
    expect(document.querySelector('form')).toBeNull();
    expect(document.body.textContent).toContain('Proprietário');
  });

  it.each(['Attendant', 'Viewer'] as UserRole[])('%s has no assignable roles', actor => {
    expect(assignableRoles(actor)).toEqual([]);
  });
});
