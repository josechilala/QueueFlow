# Renovação de sessão

Admin, Platform e Attendant mantêm access cookies de 15 minutos e refresh cookies
de 30 dias, HttpOnly, SameSite=Lax, restritos ao host e Secure em Production.
Somente 401 com o código explícito `auth.invalid_refresh` ou
`platform.invalid_refresh` remove os cookies automaticamente. 429, 5xx, timeout,
resposta inválida e conflito de rotação preservam a sessão.

Server Components redirecionam somente 401 para renovação; não rotacionam tokens
sem poder gravar cookies. Operações autenticadas dos BFFs renovam após 401 e repetem
a operação uma vez. O novo par é gravado mesmo se essa segunda operação falhar.

## Recuperação do Admin

O GET `/api/auth/refresh` encaminha para `/session/recover`, preservando um
`returnTo` local validado. Não rotaciona tokens em GET/prefetch. A tela chama um
POST de mesma origem, com Web Locks quando disponíveis para coordenar abas.
Antes de rotacionar, o POST verifica se outra requisição já gravou um access
cookie válido. Sem Web Locks, permanecem a deduplicação do BFF e o lock da API.

São feitas no máximo três tentativas por execução. `Retry-After` aceita segundos
ou data HTTP; o prazo é compartilhado entre abas, sem credenciais no storage.
Na ausência desse header, 429 aguarda 60 segundos. Falhas transitórias têm espera
progressiva de 5, 10 e 20 segundos na interface, nunca menor que `Retry-After`.
Uma tentativa manual respeita o prazo pendente. O POST pode fazer duas chamadas
de 30 segundos (/me e refresh); o cliente permite 70 segundos para essa operação.
Desmontar a tela não cancela uma rotação em andamento: o lock permanece até
consumir a resposta, permitindo que Set-Cookie seja aplicado.

O GET estabelece uma prova aleatória de 32 bytes em cookie HttpOnly/SameSite=Strict,
com duração de cinco minutos, antes da rotação. O BFF compartilha operações em
andamento por hash de origem, endpoint, token e prova. O resultado de uma rotação
Admin pode ser entregue novamente por até 60 segundos SOMENTE ao mesmo token +
prova, no mesmo processo BFF. O token antigo sozinho não recupera esse resultado.
Sem prova, o Admin compartilha apenas a operação em andamento. Nas outras
aplicações, permanece o cache bem-sucedido de cinco segundos.

Falhas temporárias ficam no cache do BFF até terminar o prazo de Retry-After
(60 segundos por padrão em 429; cinco segundos nos demais casos). O cache é
limitado a 1024 entradas, não elimina operações ativas e não grava tokens em disco.

A API continua exigindo validade, usuário ativo e rotação única em transação
com `FOR UPDATE`. Token já revogado recebe 409 sem credenciais. O BFF informa
`refresh_conflict`, preserva cookies e a tela interrompe retries automáticos.
Perda da resposta junto com reinício/troca de processo ou expiração do cache
exige novo login. A tela oferece voltar à página (caso outra aba tenha recuperado)
ou entrar novamente, preservando `returnTo`. Não existe janela de replay na API.

Dashboard e onboarding usam o transporte comum com timeout de 30 segundos.
Os logs do BFF registram somente evento, status, código e endpoint fixo;
nunca cookies, tokens, corpo de resposta ou URL com parâmetros.
A API registra a política que rejeitou requisições e correlation ID.

Restart não remove os refresh tokens do PostgreSQL nem os cookies do navegador.
Banco, issuer, audience e chave JWT devem permanecer consistentes entre instâncias.
Não há migration nova. Ver `trusted-proxies.md` para cotas, tokens assinados e
compatibilidade com tokens opacos emitidos anteriormente.
