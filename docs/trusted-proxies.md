# IP do cliente e limite de login

Login usa uma cota de **falhas de credenciais**, separada de refresh e do limite
global por IP. Consulte [a protecao de autenticacao](authentication-throttling.md)
para contagem, reset, estado Redis e configuracao. O corpo JSON continua limitado
a 16 KiB e preservado para o controller; corpos maiores recebem 413. Credenciais
nao sao registradas. O encaminhamento seguro de IP abaixo permanece necessario
para endpoints publicos e entradas de autenticacao malformadas.

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

Somente assinatura e validade verificadas permitem escolher a cota de refresh por
identidade, independentemente do IP do BFF. Rotacoes e novos logins nao reiniciam
essa cota. Tokens opacos, forjados ou malformados compartilham a cota de refresh por
IP ate receberem um envelope verificavel. A API ainda valida o token no banco;
a assinatura so determina a particao. Issuer, audience e chave JWT devem permanecer
consistentes entre instancias. Nenhuma migration ou invalidacao geral e necessaria.

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

O login nao consome mais a cota global compartilhada do BFF nem contabiliza
sucessos como falhas. LoginForm impede submit concorrente e nao repete o POST.
O BFF faz uma autenticacao, seguida das consultas autenticadas descritas acima.
Essas consultas continuam sujeitas ao limite global por usuario autenticado.
A recuperacao de sessao compartilha a requisicao em andamento na aba, usa Web Locks
entre abas quando disponivel e respeita cooldown e no maximo tres tentativas.

Os limites de login e refresh usam Redis fora de Development/Test. As demais
politicas continuam locais. A configuracao e os testes estao documentados em
[protecao de autenticacao](authentication-throttling.md). A causa de uma rejeicao
externa no ingresso precisa ser verificada nos logs desse ambiente; nenhuma
configuracao de producao foi alterada por esta correcao.
