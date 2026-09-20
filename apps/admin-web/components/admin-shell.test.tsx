import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AdminShell } from './admin-shell';
import type { AuthenticatedUser } from '../lib/auth';

const user: AuthenticatedUser = { userId: 'user', organizationId: 'tenant-a', organizationName: 'Empresa A', name: 'Ana', email: 'ana@example.test', role: 'Owner' };
describe('Admin identity', () => {
  it('shows the current organization below the email and follows the supplied tenant', () => {
    const first = renderToStaticMarkup(<AdminShell user={user}>Content</AdminShell>);
    expect(first).toContain('ana@example.test</p><p class="muted">Empresa A — Owner</p>');
    const second = renderToStaticMarkup(<AdminShell user={{ ...user, organizationId: 'tenant-b', organizationName: 'Empresa B', role: 'Viewer' }}>Content</AdminShell>);
    expect(second).toContain('Empresa B — Viewer');
    expect(second).not.toContain('Empresa A');
    expect(second).not.toContain('href="/audit"');
  });
  it('preserves the role when the API does not yet supply an organization name', () => {
    const html = renderToStaticMarkup(<AdminShell user={{ ...user, organizationName: undefined }}>Content</AdminShell>);
    expect(html).toContain('ana@example.test</p><p class="muted">Owner</p>');
  });
});
