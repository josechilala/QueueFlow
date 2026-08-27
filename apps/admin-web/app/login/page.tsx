import { Suspense } from 'react';
import { LoginForm } from './login-form';

export default function LoginPage() {
  return <main className="login-shell"><section className="login-card"><div className="brand">QueueFlow</div><p className="eyebrow">PAINEL ADMINISTRATIVO</p><h1>Entre na sua conta</h1><p className="muted">Use as credenciais cadastradas na sua organização.</p><Suspense><LoginForm /></Suspense></section></main>;
}
