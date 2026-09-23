# IP do cliente e limite de login

O limiter `auth` permanece com o padrão de 10 requisições por minuto. Nos logins
tenant e platform, a chave combina o IP efetivo da conexão com SHA-256 do e-mail
normalizado (`Trim().ToLowerInvariant()`). E-mails diferentes atrás do mesmo BFF
não dividem essa cota. O corpo JSON é limitado a 16 KiB e preservado para o controller;
corpos maiores recebem 413. Credenciais não são registradas.

Sem e-mail utilizável, e nos demais endpoints `auth`, a cota continua por IP.
Refresh usa uma política separada, `RateLimiting:RefreshPermitLimit` (padrão 30/min),
por identidade verificável nos novos tokens assinados. Tokens opacos antigos
continuam por SHA-256 do token até sua renovação; entradas sem token usam IP.
O limite global permanece em 300/min por padrão, com identidade assinada para
refresh e usuário autenticado/IP nos demais casos. Tokens forjados continuam
sujeitos à cota global por IP. Respostas 429 incluem `Retry-After`.

No Render Free, essa separação de cotas funciona pela URL pública da API sem
depender do IP real do navegador. Requisições anônimas e tokens opacos antigos
ainda compartilham o limite global pelo IP disponível do ingresso/BFF.

## Admin BFF

Iniciar com `npm run start --workspace @queueflow/admin-web` (ou o Dockerfile existente).
O `server.mjs` valida o peer TCP antes de entregar a requisição ao Next. Não substituir
esse comando por `next start`: sem a entrada validada, o Route Handler não encaminha IP.

Configurar `QUEUEFLOW_TRUSTED_PROXIES` com IPs ou CIDRs dos proxies de entrada
controlados, separados por vírgula. Sem configuração, nenhum proxy é confiável:
o endereço usado é o peer TCP. O servidor percorre X-Forwarded-For da direita para
a esquerda, parando no primeiro endereço não confiável. Headers internos recebidos
são descartados e substituídos por uma prova HMAC local, com chave aleatória por processo;
`QUEUEFLOW_INTERNAL_IP_KEY` é definido pelo servidor e não precisa ser provisionado.

## API

Configurar os peers autorizados a encaminhar IP com:

- `ReverseProxy__KnownProxies__0`, `ReverseProxy__KnownProxies__1`, etc.: IPs exatos.
- `ReverseProxy__KnownNetworks__0`, etc.: CIDRs restritos, se IPs exatos não forem viáveis.
- `ReverseProxy__ForwardLimit`: número máximo de saltos autorizados (padrão 1, máximo 32).

Para BFF → API diretamente na rede privada, confiar no IP do BFF e usar limite 1.
Se houver outro proxy entre eles, confiar também nesse proxy e configurar o número
de saltos necessário. Chamadas diretas pelo ingresso público precisam incluir esse
ingresso na lista. Sem configuração a API ignora os headers encaminhados.

Todos os proxies autorizados devem sobrescrever X-Forwarded-For com o peer real ou
acrescentá-lo à direita; nunca repassar intacta uma cadeia fornecida pelo cliente.
Não confiar em faixas públicas compartilhadas de saída, na rede privada inteira ou
em endereços de clientes. Usar somente redes exclusivas/controladas e restringir o
acesso à origem. `/0` é rejeitado. Não ativar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`,
que pode instalar processamento adicional fora desta configuração explícita.

No Render, identificar a cadeia real e os IPs/redes controlados antes de configurar;
nenhuma faixa foi presumida no código. A alteração sozinha não identifica esses peers
nem modifica o ambiente de produção. Se a plataforma não permitir estabelecer essa
confiança, é necessário um ingresso controlado antes de habilitar o encaminhamento.

Referências: [Next custom server](https://nextjs.org/docs/app/guides/custom-server)
e [ASP.NET Core forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).


## Identidade verificável no refresh

Novos refresh tokens carregam um envelope HMAC com propósito próprio, identidade
tenant/platform, usuário, validade e 64 bytes aleatérios. O envelope NÃO é um
access token e NÃO autoriza operações: a API ainda exige o hash exato persistido,
usuário ativo, validade e rotação única sob `FOR UPDATE`.

Somente assinatura e validade verificadas permitem escolher as cotas global e
refresh por identidade, independentemente do IP do BFF. Rotações e novos logins
do mesmo usuário não reiniciam essas cotas. Os limites continuam 300/min global,
30/min refresh e 10/min login por padrão. Assinaturas falsas, identidades de outro
propósito e entradas malformadas continuam na proteção global por IP. Não há
consulta ao banco antes do limiter, nem confiança em headers enviados pelo cliente.

Compatibilidade: tokens opacos já emitidos continuam funcionando sob as cotas
anteriores por IP/token até a primeira renovação ou novo login, quando recebem
o novo formato. Nenhuma migration ou invalidação geral de sessões é necessária.
A API deve ser implantada antes de avaliar a separação das cotas em sessões novas;
issuer, audience e chave JWT devem permanecer consistentes entre instâncias.

Toda rejeição registra política, template do endpoint (sem valores dos parâmetros
ou query string), correlation ID, RetryAfter, IP resolvido, peer original e
X-Forwarded-For sanitizado antes do processamento dos proxies. O header é limitado
a 2048 caracteres/32 entradas; somente IPs válidos entram no log, outras entradas
viram `invalid`. Headers maiores viram `oversized`.
A partição aparece como HMAC com chave aleatória por processo, permitindo comparar
buckets na mesma instância sem expor e-mail, hash de token ou chave original.
`Instance` distingue processos; HMACs não são comparáveis entre reinícios/instâncias.
Senhas, JWTs, cookies e corpos das requisições não são registrados.
A resposta da API continua informando `X-RateLimit-Policy` e `Retry-After`.

## Investigação de 429 no login

Um 429 em `/api/auth/login` do Admin não prova que o POST de autenticação foi
bloqueado: o BFF também propaga 429 das consultas `/api/v1/auth/me`,
`/api/v1/onboarding` e do POST `/api/v1/onboarding/complete`. O novo header
`X-RateLimit-Endpoint` e o evento `admin_login_rate_limited` identificam a etapa.
Esse endpoint é uma constante local; headers arbitrários do upstream não definem
o valor. Política ausente/desconhecida é registrada como `unknown`, sem atribuir
automaticamente a rejeição à API ou ao Render.

Configurações padrão no código (overrides do ambiente de produção precisam ser conferidos):

| Política | PermitLimit | Window | QueueLimit | Partição |
| --- | ---: | --- | ---: | --- |
| global | 300 | 1 minuto | 0 | identidade verificável no refresh; senão NameIdentifier autenticado; senão IP/unknown |
| auth | 10 | 1 minuto | 0 | login: IP + SHA-256 do e-mail normalizado; demais casos: IP |
| refresh | 30 | 1 minuto | 0 | identidade verificável; senão hash do token; senão IP |
| public | 30 | 1 minuto | 0 | IP/unknown |
| trial-requests | 5 | 10 minutos | 0 | IP/unknown |

São fixed windows com reposição automática. O global é adquirido primeiro, depois
a política do endpoint. Não existe limiter no servidor Next: há bloqueio de submit
concorrente e cooldown no formulário, sem repetição automática do POST. O middleware
de navegação do Admin não intercepta `/login` nem `/api/auth/login`.
`auth` é compartilhada entre login tenant/platform quando IP e e-mail coincidem.
Todas as tentativas consomem quota, inclusive credenciais corretas: o limiter roda
antes do controller e da verificação da senha. Não se trata de bloqueio persistido
na conta por número de senhas erradas.

O teste `SharedProxyGlobalBudgetCanBlockAnEmailWithNoPreviousLoginAttempts`
reproduz o risco: requisições anônimas do mesmo IP esgotam o global e bloqueiam um
e-mail novo mesmo sem esgotar `auth`. Separar e-mails em `auth` não elimina esse
compartilhamento global. O teste usa limite global reduzido apenas no host de teste.

Sem `QUEUEFLOW_TRUSTED_PROXIES` no Admin e sem `ReverseProxy:KnownProxies`/
`KnownNetworks` na API, cada camada usa seu peer TCP. Mesmo com trust configurado,
ForwardLimit insuficiente pode parar no BFF/proxy intermediário. Não há configuração
de produção versionada que permita afirmar quais IPs estão sendo usados no Render.
Conferir cadeia real, comando de início do Admin, valores efetivos das cinco
configurações `RateLimiting:*PermitLimit` e logs da mesma tentativa antes de alterar
trust ou limites. Nunca confiar em `/0` ou simplesmente no primeiro IP do header.

Os limiters são locais à memória do processo, sem Redis/banco. Restart/deploy reinicia
as cotas; múltiplas instâncias mantêm contadores independentes. Cold start pode
adicionar latência, mas não esgota sozinho uma quota recém-criada. Um bloqueio depois
de inatividade exige verificar tráfego concorrente, configuração, etapa exata e
eventual rejeição no ingresso. Não foi comprovada a causa específica de produção
apenas pelo sintoma de 429 com Retry-After.
