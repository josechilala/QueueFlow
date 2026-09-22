export function loginError(status: number): string {
  if (status === 401) return 'E-mail ou senha inválidos.';
  if (status === 429) return 'Excesso de tentativas. Aguarde antes de tentar novamente.';
  if (status >= 500) return 'Serviço de autenticação temporariamente indisponível. Aguarde e tente novamente.';
  if (status === 400) return 'Informe um e-mail e uma senha válidos.';
  return 'Não foi possível entrar. Tente novamente mais tarde.';
}
