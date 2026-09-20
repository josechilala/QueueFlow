# IP do cliente e limite de login

O limiter `auth` permanece com o padrão de 10 requisições por minuto. Nos logins
tenant e platform, a chave combina o IP efetivo da conexão com SHA-256 do e-mail
normalizado (`Trim().ToLowerInvariant()`). E-mails diferentes atrás do mesmo BFF
não dividem essa cota. O corpo JSON é limitado a 16 KiB e preservado para o controller;
corpos maiores recebem 413. Credenciais não são registradas.

Sem e-mail utilizável, e nos demais endpoints `auth`, a cota continua por IP.
Refresh usa uma política separada, `RateLimiting:RefreshPermitLimit` (padrão 30/min),
por SHA-256 do refresh token, sem normalizar seu conteúdo. Requisições sem token
utilizável usam uma cota por IP nessa política. Assim, sessões diferentes atrás
do BFF não dividem a cota de refresh nem consomem a cota de login.
O limite global por usuário/IP permanece ativo (padrão 300/min), limitando também
tentativas com e-mails ou tokens variados. Não há confiança adicional em headers.
Respostas 429 incluem `Retry-After` quando disponibilizado pelo limiter.

No Render Free, essa separação de cotas funciona via URL pública da API sem configurar
proxies confiáveis: o IP disponível pode ser o do ingresso/BFF. O limite global continua
compartilhado por esse IP, e a solução não pretende descobrir o IP real do navegador.

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
