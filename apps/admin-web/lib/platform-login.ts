export function platformLoginError(status: number): string {
  if (status === 401) return 'E-mail ou senha inválidos.';
  if (status === 429) return 'Excesso de tentativas. Aguarde um pouco antes de tentar novamente.';
  if (status >= 500) return 'Serviço de autenticação indisponível. Tente novamente mais tarde.';
  if (status === 400) return 'Informe um e-mail e uma senha válidos.';
  return 'Não foi possível entrar. Tente novamente mais tarde.';
}

export async function submitPlatformLogin(email: FormDataEntryValue | null, password: FormDataEntryValue | null): Promise<string | null> {
  try {
    const response = await fetch('/api/platform/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
    if (!response.ok) return platformLoginError(response.status);
    const body = await response.json();
    return body?.authenticated === true ? null : platformLoginError(502);
  } catch {
    return platformLoginError(502);
  }
}
