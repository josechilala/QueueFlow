'use client';
import { configuredUrl } from '../../../lib/configured-url';
import { FormEvent, useEffect, useRef, useState } from 'react';

const apiUrl = configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL, '', true);
const adminUrl = configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_ADMIN_URL, '', true);
type Invitation = { maskedEmail: string; organizationName: string | null; status: string; canRequestVerificationCode: boolean; isVerified: boolean };
type Stage = 'loading' | 'verify' | 'complete' | 'done' | 'error';

export function ActivationFlow({ token }: { token?: string }) {
  const [activationAuthorization, setActivationAuthorization] = useState('');
  const [email, setEmail] = useState('');
  const [organizationName, setOrganizationName] = useState('');
  const [invitation, setInvitation] = useState<Invitation>();
  const [stage, setStage] = useState<Stage>('loading');
  const [message, setMessage] = useState('');
  const [devCode, setDevCode] = useState('');
  const [pending, setPending] = useState(false);
  const inFlight = useRef(false);
  const completed = useRef(false);

  useEffect(() => {
    if (token === undefined) return;
    const controller = new AbortController();
    let active = true;
    fetch(apiUrl + '/api/v1/public/activation/lookup', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ invitationToken: token }), cache: 'no-store', signal: controller.signal,
    }).then(async response => {
      if (!response.ok) throw new Error();
      const data = await response.json() as Invitation;
      if (!['Pending', 'Verified'].includes(data.status)) throw new Error();
      if (!active || completed.current) return;
      setInvitation(data);
      setOrganizationName(data.organizationName ?? '');
      setStage('verify');
    }).catch(() => {
      if (!active || completed.current) return;
      setMessage('Este convite n?o est? dispon?vel.');
      setStage('error');
    });
    return () => { active = false; controller.abort(); };
  }, [token]);

  function begin() {
    // A ref closes the gap before React renders the disabled buttons.
    if (inFlight.current || completed.current) return false;
    inFlight.current = true;
    setPending(true);
    setMessage('');
    return true;
  }
  function finish() { inFlight.current = false; setPending(false); }
  function post(path: string, body: object) {
    return fetch(apiUrl + '/api/v1/public/activation/' + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), cache: 'no-store',
    });
  }

  async function requestCode() {
    if (stage !== 'verify' || !begin()) return;
    try {
      const response = await post('request-code', { invitationToken: token });
      const data = await response.json().catch(() => null) as { developmentCode?: string; detail?: string } | null;
      if (!response.ok) { setMessage(data?.detail ?? 'N?o foi poss?vel enviar o c?digo.'); return; }
      setDevCode(data?.developmentCode ?? '');
      setMessage('Enviamos um c?digo para o e-mail do convite.');
    } catch { setMessage('N?o foi poss?vel enviar o c?digo. Tente novamente.'); }
    finally { finish(); }
  }

  async function verify(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (stage !== 'verify' || !begin()) return;
    const code = new FormData(event.currentTarget).get('code');
    try {
      const response = await post('verify', { invitationToken: token, code });
      const grant = await response.json().catch(() => null) as { activationAuthorization?: string } | null;
      if (!response.ok || !grant?.activationAuthorization) { setMessage('C?digo inv?lido ou expirado.'); return; }
      setActivationAuthorization(grant.activationAuthorization);
      setStage('complete');
      setMessage('E-mail confirmado.');
    } catch { setMessage('N?o foi poss?vel confirmar o c?digo. Tente novamente.'); }
    finally { finish(); }
  }

  async function complete(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (stage !== 'complete' || !activationAuthorization || !begin()) return;
    const form = event.currentTarget;
    const values = new FormData(form);
    try {
      const response = await post('complete', {
        invitationToken: token,
        activation: { activationAuthorization, organizationName: values.get('organizationName'), slug: values.get('slug') || null,
          responsibleName: values.get('responsibleName'), password: values.get('password'),
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'America/Sao_Paulo' },
      });
      if (response.status === 200) {
        // The API has committed. Success is terminal, even if reading its JSON body fails.
        completed.current = true;
        setActivationAuthorization('');
        const password = form.elements.namedItem('password') as HTMLInputElement | null;
        if (password) password.value = '';
        setMessage('');
        setStage('done');
        setPending(false);
        const data = await response.json().catch(() => null) as { email?: string } | null;
        if (typeof data?.email === 'string') setEmail(data.email);
        return;
      }
      const data = await response.json().catch(() => null) as { detail?: string } | null;
      if (response.status === 404) { setActivationAuthorization(''); setStage('verify'); }
      setMessage(data?.detail ?? 'N?o foi poss?vel concluir a ativa??o.');
    } catch {
      if (!completed.current) setMessage('N?o foi poss?vel confirmar o resultado da ativa??o. Verifique seu acesso ao Admin antes de tentar novamente.');
    } finally { finish(); }
  }

  const adminLogin = adminUrl ? adminUrl + '/login?activated=1' + (email ? '&email=' + encodeURIComponent(email) : '') : undefined;
  return <section className="activation-flow" aria-busy={pending}>
    {/* Keep panels and text containers mounted: stage changes never remove translated DOM nodes. */}
    <div hidden={stage !== 'loading'}><p>Carregando convite?</p></div>
    <div hidden={stage !== 'error'}><h1>Convite indispon?vel</h1></div>
    <div hidden={stage !== 'verify' && stage !== 'complete'}>
      <h1>Ative sua organiza??o</h1>
      <p><span>Convite para </span><strong>{invitation?.organizationName ?? 'sua empresa'}</strong><span>.</span></p>
      <p className="muted"><span>Confirmaremos o e-mail </span><span>{invitation?.maskedEmail ?? ''}</span><span> antes da cria??o.</span></p>
    </div>
    <div hidden={stage !== 'verify'}>
      <button type="button" onClick={requestCode} disabled={pending || stage !== 'verify' || !invitation?.canRequestVerificationCode}>Enviar c?digo</button>
      <p className="muted" hidden={!devCode}><span>C?digo de desenvolvimento: </span><span>{devCode}</span></p>
      <form className="booking-form" onSubmit={verify}>
        <label><span>C?digo de 6 d?gitos</span><input name="code" inputMode="numeric" pattern="[0-9]{6}" required disabled={pending || stage !== 'verify'} /></label>
        <button type="submit" disabled={pending || stage !== 'verify'}>Confirmar e-mail</button>
      </form>
    </div>
    <div hidden={stage !== 'complete'}>
      <form className="booking-form" onSubmit={complete}>
        <label><span>Nome da empresa</span><input name="organizationName" value={organizationName} onChange={event => setOrganizationName(event.target.value)} required disabled={pending || stage !== 'complete'} /></label>
        <label><span>Slug p?blico (opcional)</span><input name="slug" disabled={pending || stage !== 'complete'} /></label>
        <label><span>Seu nome</span><input name="responsibleName" required disabled={pending || stage !== 'complete'} /></label>
        <label><span>Crie uma senha</span><input name="password" type="password" minLength={12} required disabled={pending || stage !== 'complete'} /></label>
        <button type="submit" disabled={pending || stage !== 'complete'}>Ativar e entrar na configura??o</button>
      </form>
    </div>
    <div hidden={stage !== 'done'} role="status">
      <h1>Conta ativada</h1>
      <p>Sua organiza??o foi criada. Agora entre no painel administrativo com o e-mail convidado.</p>
      <a className="service-action" hidden={!adminUrl} href={adminLogin}>Ir para o Admin</a>
      <p hidden={!!adminUrl}>Endere?o do Admin n?o configurado.</p>
    </div>
    <p className="muted" hidden={!pending} role="status">Processando?</p>
    <p className="muted" hidden={!message || stage === 'done'} role="status"><span>{message}</span></p>
  </section>;
}
