'use client';
import { FormEvent, useState } from 'react';
import { useRouter } from 'next/navigation';
import type { Branch } from '../../lib/branches';
import { assignableRoles, roleLabels, type UserRole } from '../../lib/users';

export function UserCreateForm({ actorRole, branches }: { actorRole: UserRole; branches: Branch[] }) {
  const router = useRouter(); const [error, setError] = useState(''); const [pending, setPending] = useState(false); const [role, setRole] = useState<UserRole>(assignableRoles(actorRole)[0]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const formElement = event.currentTarget; const form = new FormData(formElement); setPending(true); setError('');
    const response = await fetch('/api/users', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: form.get('name'), email: form.get('email'), password: form.get('password'), role, branchIds: form.getAll('branchIds') }) });
    if (response.status === 401) { window.location.assign('/login'); return; }
    if (!response.ok) { const body = await response.json().catch(() => null); setError(body?.detail ?? 'Não foi possível cadastrar o usuário.'); setPending(false); return; }
    formElement.reset(); router.refresh(); setPending(false);
  }
  return <form className="entity-form" onSubmit={submit}><h3>Novo usuário</h3>
    <label>Nome<input name="name" required minLength={2} maxLength={200} /></label><label>E-mail<input name="email" type="email" required /></label><label>Senha inicial<input name="password" type="password" required minLength={12} /></label>
    <label>Perfil<select name="role" value={role} onChange={event => setRole(event.target.value as UserRole)}>{assignableRoles(actorRole).map(value => <option key={value} value={value}>{roleLabels[value]}</option>)}</select></label>
    <fieldset><legend>Unidades {role === 'Attendant' && '(obrigatório)'}</legend>{branches.filter(x => x.isActive).map(branch => <label className="check-option" key={branch.id}><input type="checkbox" name="branchIds" value={branch.id} /> {branch.name}</label>)}</fieldset>
    {error && <p className="form-error">{error}</p>}<button disabled={pending}>{pending ? 'Salvando...' : 'Cadastrar usuário'}</button>
  </form>;
}
