# Revisão final — Partes 1, 2, 3 e 4

> Histórico da revisão inicial. Os achados foram tratados no [relatório de fechamento](closure-parts-1-4.md), que contém o estado e as validações atuais.

**Resultado: B) NÃO APROVADO PARA COMMIT.**

Data: 15/09/2026. Escopo: os 74 pontos do anexo de revisão, implementação atual, testes e migrations locais.

A revisão encontrou falhas de segurança reproduzíveis. A suíte existente passou, mas não cobre os cenários descritos abaixo. Não foi implementado provider de e-mail nem alterado código da aplicação. Um snapshot SHA-256 dos 419 arquivos existentes confirmou zero alterações durante a revisão. Este relatório é uma nova entrega documental; os probes estão em .artifacts/final-review, ignorado pelo Git.

A instrução “Se exigir decisão arquitetural: PARE e relate” foi aplicada às correções: nenhuma mudança de contrato, sessão, identidade ou schema foi feita. Os testes solicitados e as reproduções locais foram concluídos.

## 1. Achados por prioridade

### R1 — P1: verificação de e-mail não vincula a autorização de ativação ao cliente que verificou

**Local:** src/QueueFlow.Application/Features/Platform/OrganizationInvitationService.cs:139 e :151.

VerifyCodeAsync grava VerifiedAt globalmente. CompleteActivationAsync aceita token do convite e VerifiedAt não nulo, sem exigir código ou outra prova obtida pelo navegador que o verificou.

Reprodução com dois HttpClients independentes:

1. Cliente A recebe e confirma o código: HTTP 204.
2. Cliente B, que conhece o link mas não recebeu o código, chama complete com senha escolhida por B: HTTP 200.
3. B autentica como Owner com essa senha: HTTP 200.

O criador do convite conserva o link inicialmente. Assim, um PlatformAdmin que guarde esse bearer token também pode disputar a ativação após o destinatário verificar o e-mail, usando a rota anônima. O bloqueio do JWT platform nos endpoints tenant não impede esse caminho.

**Impacto:** tomada da conta no intervalo entre verificar e ativar.

**Decisão necessária:** exigir o código na transação final de ativação, ou emitir uma autorização de ativação curta, consumível e vinculada ao cliente que verificou. O estado VerifiedAt sozinho não pode autorizar a definição da senha. Nenhuma opção foi implementada nesta revisão.

### R2 — P1: token privado de agendamento é transmitido ao grupo SignalR público do serviço

**Locais:**
- src/QueueFlow.Infrastructure/Jobs/OutboxProcessor.cs:54–60.
- src/QueueFlow.Infrastructure/Realtime/SignalRQueueRealtimeNotifier.cs:24–53.
- src/QueueFlow.Infrastructure/Realtime/QueueHub.cs:31–38.

O outbox envia appointment.realtime para QueueEventAsync(servicePublicId, ...). O notifier só remove campos contendo “token” quando encontra uma Queue. Para um Service.PublicId, a busca não encontra Queue e envia o payload integral, incluindo appointmentToken, ao grupo queue:{servicePublicId}.

JoinQueueGroup é público e aceita esse ID de serviço. Ele está disponível no catálogo público.

Reprodução:
- Notifier real, com IHubContext capturado: payload do grupo público contém appointmentToken.
- GET anônimo do agendamento com esse token: HTTP 200.
- Cancelamento anônimo com o mesmo token: HTTP 200.

**Impacto:** um visitante do catálogo pode acompanhar eventos e obter capacidade de consultar/cancelar/reagendar agendamentos de outras pessoas.

**Correção indicada:** payload público estritamente permitido por lista de campos, aplicado também aos grupos de serviço. Tokens privados devem ficar exclusivamente nos grupos privados correspondentes. A correção não foi aplicada após a parada para decisão de R1.

Limite da reprodução: a captura adicional ocorreu no IHubContext, não em um navegador/WebSocket; o acesso anônimo ao grupo foi confirmado no código e a utilização do token foi confirmada via HTTP real do TestServer.

### R3 — P1: token puro de convite aparece nos logs de Production

**Locais:** PublicActivationController.cs:10–13; Program.cs:181–185; ObservabilityExtensions.cs:21.

A API inclui o bearer token no caminho /api/v1/public/activation/{token}. O logging de requisições ASP.NET/Serilog registra esse caminho sem redação. A instrumentação HTTP também não exclui/redige a rota.

Reprodução em factory com environment Production:
- GET de metadados de convite.
- O sink de logs recebeu o token puro: production_invitation_token_logged = true.

**Impacto:** leitores de logs passam a ter acesso ao link de ativação. A tabela armazena somente TokenHash, mas o segredo escapa por outro canal.

**Correção indicada:** definir como o token transitará e como API, Customer, proxy e tracing evitarão seu registro. Se mantido na URL, são necessárias redações consistentes em todas essas camadas. Alterar o transporte para corpo e/ou fragmento requer avaliar o contrato do link.

**Distinção:** o código numérico de verificação não foi exposto nos testes de Production. A falha confirmada é do token do convite.

### R4 — P2: novos agendamentos e mudanças de estado não atualizam a lista do atendente pelo canal atual

**Locais:** apps/attendant-web/app/workstation/realtime-refresh.tsx:5; OutboxProcessor.cs:60; SignalRQueueRealtimeNotifier.cs:24.

O atendente assina grupos de Queue e escuta somente QueueUpdated. Eventos appointment.* são publicados no grupo público do Service, sem QueueUpdated quando não é encontrada uma Queue.

A reprodução do notifier confirmou appointment_event_emits_QueueUpdated = false. A consulta “hoje” está correta quando executada, mas a tela já aberta pode ficar desatualizada até um refresh manual ou outro evento de fila.

**Correção indicada:** alinhar assinaturas e eventos de atualização, mantendo o payload público sem credenciais. O teste SignalR existente de filas passou, inclusive check-in/outbox; ele não cobre chegada de novo agendamento remoto à lista aberta do atendente.

### R5 — P2: nova unicidade global de e-mail produz erro 500 no cadastro de equipe

**Local:** src/QueueFlow.Application/Features/Users/UserManagementService.cs:44–52.

CreateAsync verifica duplicidade com filtro tenant. Um e-mail já usado em outra organização não é detectado; o índice global rejeita o INSERT e a API devolve 500.

Reprodução: criar Viewer no tenant B usando e-mail pertencente ao tenant A → HTTP 500.

A constraint preserva a unicidade e não houve mistura de usuários, mas o fluxo ficou inconsistente com a regra global.

**Correção indicada:** validação global sem retornar dados do outro tenant e tratamento da violação de unicidade, incluindo concorrência, como conflito de negócio.

### R6 — P2: login abandona onboarding assim que existe uma unidade

**Local:** apps/admin-web/app/api/auth/login/route.ts:14–19.

needsOnboarding depende apenas de não existir Branch ativa. Crie uma unidade, saia antes de cadastrar serviços/filas/agenda e entre novamente: o login leva ao Dashboard, embora o onboarding ainda esteja incompleto.

Os dados já cadastrados permanecem; o redirecionamento não usa o mesmo critério de conclusão de /onboarding.

**Correção indicada:** compartilhar um critério de progresso/conclusão entre login e onboarding. Avaliar explicitamente se “concluído uma vez” precisa ser distinguido de mudanças operacionais posteriores.

### R7 — P2: preflight SQL não usa exatamente a normalização da autenticação

**Local:** migration 20260915060310_AddPlatformAdministrationAndInvitations.cs:26; scripts/preflight-global-user-email.sql.

O login usa Trim().ToLowerInvariant(). A migration usa lower(btrim(Email)), e o índice único incide sobre Email bruto.

Reprodução mínima:
- .NET considera "\tuser@example.test" e "user@example.test" iguais após normalização.
- PostgreSQL lower(btrim(...)) considera-os diferentes, pois o btrim padrão não retira tabulação.

A CLI --preflight-user-email faz a normalização .NET correta, mas a migration aplicada diretamente por EF/--migrate-only não a chama.

O caminho normal de criação AppUser já normaliza, reduzindo o risco para dados originados exclusivamente na aplicação. Não foi comprovado que os dados históricos do Render satisfazem essa premissa.

**Correção indicada:** garantir uma pré-condição de dados canônicos/normalização equivalente antes de permitir a migration. Não corrigir ou excluir registros automaticamente.

### Observações adicionais de sessão e configuração

- Tenant e platform usam cookies diferentes e logout de um não apaga os cookies do outro.
- Admin e Attendant, quando servidos no mesmo host em portas diferentes, apagam cookies um do outro: admin-web/lib/auth-cookies.ts:15 e attendant-web/lib/auth-cookies.ts:8–9. Confirmar se são sessões deliberadamente acopladas.
- O login Admin de um Attendant grava cookie no host Admin e redireciona ao host Attendant. Em domínios distintos do Render, esse cookie não é compartilhado; o destino pedirá login novamente. Não há SSO implementado. Não inserir JWT em URL para resolver isso.
- Refresh/logout platform constroem redirecionamentos com request.url, enquanto as rotas tenant usam publicUrl. QUEUEFLOW_PUBLIC_URL não é usado nesses dois handlers platform; validar/corrigir atrás do proxy do Render.
- A central de links não contém fallback localhost/onrender. Existem defaults localhost em outros pontos (API/realtime e redirecionamento do login para Attendant). As variáveis de produção são obrigatórias para evitar esses defaults.

## 2. O que foi confirmado

- PlatformUser separado de AppUser, sem OrganizationId e fora de ITenantEntity.
- PlatformRefreshToken e PlatformAuditLog separados.
- JWT tenant/platform emitidos com identity_type apropriado.
- Oito policies tenant exigem tenant, uma organização GUID válida/não vazia e ausência de duplicação dessas claims.
- Todos os endpoints administrativos platform estão sob RequirePlatformAdmin. Login e refresh são exceções públicas intencionais; protegê-los com access JWT impediria login/renovação. Usam credencial/refresh platform e rate limit.
- JWT platform rejeitado nas rotas tenant e JWT tenant rejeitado nas platform.
- ApplicationDbContext rejeita SaveChanges/SaveChangesAsync com escrita ITenantEntity por identidade platform, inclusive com OrganizationId.
- Não existe outro endpoint público de criação de Organization além de register controlado e ativação por convite.
- register exige flag explícita e Development/Test; Production com flag indevidamente true continua fechado.
- Tokens de convite são aleatórios de 256 bits; somente hash persiste na entidade/tabela.
- Reemissão/revogação/ativação usam lock de linha; single-use e concorrência de ativação foram testados.
- Ativação é transacional; falha injetada ao atualizar UsedAt reverte Organization, Owner e Subscription.
- Código usa HMAC/pepper, cooldown, expiração e tentativas; Production força a exposição para false.
- Refresh tenant em endpoint platform retorna 401; refresh platform em endpoint tenant retorna 401.
- Bootstrap é CLI explícita, protegida por configuração e advisory lock; não roda no startup normal; usa PasswordService; senha lida do ambiente.
- Agendamentos do atendente são filtrados pelas unidades atribuídas; check-in idempotente e associação por IDs foram testados.
- A seleção temporal por ScheduledStart permanece inalterada; testes de política híbrida e concorrência passaram.

A ausência de autenticação nos serviços públicos legítimos não torna ApplicationDbContext uma barreira universal: identidades anônimas seguem permitidas para criação pública de tickets/ativação. A autorização desses fluxos precisa estar correta nos serviços, como demonstra R1.

## 3. Migrations, PostgreSQL e Render

### Inspeção e execução local

- Migration nova cria PlatformUsers, PlatformRefreshTokens, OrganizationInvitations e PlatformAuditLogs.
- Preflight SQL precede remoção do índice antigo e criação de IX_Users_Email.
- O índice global é único; TokenHash também é único.
- ActivatedOrganizationId tem unicidade filtrada para valores não nulos.
- Não há normalização, exclusão ou escolha automática de usuários no Up.
- Teste com duplicidade simples abortou a migration e preservou os dois usuários.
- Up → Down → Up foi repetido em bancos temporários, com preservação dos dados tenant.
- O Down remove as quatro tabelas platform e seus dados, recriando a unicidade antiga. Não desfaz Organizations/Owners já ativados. Isso exige backup e decisão operacional antes de qualquer rollback real.
- Os scripts up/down existentes foram revisados. Não foram aplicados no Render.

### Locks e risco

A migration usa transação e LOCK TABLE Users IN SHARE ROW EXCLUSIVE MODE antes do preflight. Bloqueia alterações concorrentes em Users durante a operação; a leitura/DDL do índice também exige recursos e locks. O EF serializa migrations por lock de __EFMigrationsHistory.

Não existe estratégia de índice CONCURRENTLY nessa migration. Prever janela de manutenção, drenagem de transações, backup e avaliação em cópia com volume real. Não estimar duração a partir do pequeno banco temporário.

### Compatibilidade com o banco atualmente no Render: NÃO CONFIRMADA

Foram inspecionados os nomes das configurações locais sem imprimir credenciais. Não há conexão Render disponível nas variáveis do processo ou no connection string local identificado. Nenhuma consulta foi feita no banco hospedado.

Compatibilidade local com PostgreSQL 17 não comprova o estado real do Render. Faltam evidências somente leitura de:

1. versão do PostgreSQL e collation/encoding;
2. __EFMigrationsHistory e schema atual;
3. índice IX_Users_OrganizationId_Email e ausência de deriva manual;
4. duplicidades e dados não canônicos sob a normalização .NET;
5. volume de Users e transações/locks concorrentes;
6. permissões do usuário que executará a migration.

Essa verificação não exige aplicar migration, criar administrador ou modificar secrets. Nesta revisão ela permanece não executada.

## 4. Variáveis para o Render, por aplicação

Não houve alteração de variáveis no Render. “Nova” significa introduzida ou necessária para os fluxos revisados; variáveis já usadas aparecem como dependências existentes. Valores de exemplo não são credenciais reais.

### API

Todas são de **runtime**, inclusive as variáveis one-time da CLI; não colocar secrets na imagem/build.

| Variável | Nova/existente | Secret? | Necessidade |
| --- | --- | --- | --- |
| Onboarding__PublicUrl | Nova | Não | Origem HTTPS do Customer para montar o link; obrigatória para emitir/reemitir/enviar convite |
| Onboarding__VerificationCodePepper | Nova | Sim | Segredo HMAC forte, externo e estável entre instâncias; obrigatório para verificação |
| Onboarding__InvitationExpirationHours | Nova, opcional | Não | Default 24 |
| Onboarding__VerificationCodeExpirationMinutes | Nova, opcional | Não | Default 10 |
| Onboarding__VerificationCodeCooldownSeconds | Nova, opcional | Não | Default 60 |
| Onboarding__VerificationCodeMaxAttempts | Nova, opcional | Não | Default 5 |
| Onboarding__AllowPublicRegistration | Nova | Não | Manter false/ausente em Production; Production bloqueia mesmo true |
| Onboarding__ExposeVerificationCodeForDevelopment | Nova | Não | Manter false/ausente em Production; forçada false fora Development/Test |
| PlatformBootstrap__Enabled | Nova, one-time | Não | true somente durante provisionamento aprovado; remover/desabilitar depois |
| PlatformBootstrap__Email | Nova, one-time | Dado pessoal/configuração | E-mail do primeiro administrador |
| PlatformBootstrap__Name | Nova, one-time, opcional | Dado pessoal/configuração | Nome; há default |
| PlatformBootstrap__Password | Nova, one-time | Sim | Senha por environment somente durante a CLI; remover depois |
| ConnectionStrings__QueueFlowDatabase | Existente | Sim | Conexão PostgreSQL correta |
| Authentication__JwtKey | Existente | Sim | Chave JWT tenant/platform; não há segunda chave obrigatória implementada |
| Authentication__Issuer / Authentication__Audience | Existentes | Não | Padrões QueueFlow / QueueFlow.Web; manter coerentes |
| ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT | Existentes | Não | Production; evitar valores conflitantes e nunca usar Development/Test no Render produtivo |
| Cors__Origins__0, __1, ... | Existentes | Não | Origens HTTPS explícitas dos frontends |
| Redis__ConnectionString | Existente, conforme topologia | Sim, se contém credenciais | Backplane entre API/Worker/instâncias |
| Redis__UseBackplane | Existente | Não | true quando eventos devem atravessar processos |
| RateLimiting__GlobalPermitLimit / PublicPermitLimit / AuthPermitLimit | Existentes, opcionais | Não | Defaults 300/30/10 por minuto; limites locais ao processo |
| OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_PROTOCOL | Existentes, opcionais | Conforme configuração | Telemetria; considerar R3 antes de exportar rotas sensíveis |

Nenhuma variável de fornecedor de e-mail pode ser prescrita como implementada: o fornecedor/adaptador real ainda não existe. Seus secrets dependerão da implementação escolhida posteriormente.

### Admin Web

| Variável | Secret? | Momento | Finalidade |
| --- | --- | --- | --- |
| QUEUEFLOW_API_URL | Não, se URL sem credenciais | Runtime | Acesso server-side à API; existente |
| QUEUEFLOW_PUBLIC_URL | Não | Runtime | Origem pública HTTPS do Admin; redirects e configuração da central |
| QUEUEFLOW_CUSTOMER_URL | Não | Runtime | Origem pública HTTPS do Customer; links presencial/agendamento |
| NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL | Não | Build-time | Link do atendente e redirecionamento de login; manter também no ambiente de build Docker ARG |
| NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL | Não | Build-time | Link público do display |
| NEXT_PUBLIC_QUEUEFLOW_API_URL | Não | Build-time | SignalR/browser; existente |
| QUEUEFLOW_SECURE_COOKIES | Não | Runtime | true em HTTPS produtivo; default produtivo já é true |
| NODE_ENV | Não | Build/runtime do Next | production |

NEXT_PUBLIC_* é público e incorporado ao bundle: modificar apenas o runtime após o build não é suficiente.

### Customer Web

| Variável | Secret? | Momento | Finalidade |
| --- | --- | --- | --- |
| NEXT_PUBLIC_QUEUEFLOW_ADMIN_URL | Não | Build-time | Nova: destino /login após ativação; obrigatória para apontar ao Admin |
| NEXT_PUBLIC_QUEUEFLOW_API_URL | Não | Build-time | API usada pelo browser na ativação e no realtime; existente |
| QUEUEFLOW_API_URL | Não, se URL sem credenciais | Runtime | SSR/proxies existentes |
| NODE_ENV | Não | Build/runtime | production |

Customer não recebe pepper, chave JWT nem senha platform.

### Attendant Web

Nenhuma variável nova exclusiva dos agendamentos de hoje. Conferir as existentes:

| Variável | Secret? | Momento | Finalidade |
| --- | --- | --- | --- |
| QUEUEFLOW_API_URL | Não, se URL sem credenciais | Runtime | Consultas e comandos server-side |
| NEXT_PUBLIC_QUEUEFLOW_API_URL | Não | Build-time | SignalR no browser |
| QUEUEFLOW_PUBLIC_URL | Não | Runtime | Origem HTTPS do Attendant para redirects |
| QUEUEFLOW_SECURE_COOKIES | Não | Runtime | true em HTTPS |
| NODE_ENV | Não | Build/runtime | production |

NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL é configurada no Admin que produz o link, não é uma nova variável necessária no Attendant.

### Display Web

Nenhuma variável nova exclusiva da central:

| Variável | Secret? | Momento | Finalidade |
| --- | --- | --- | --- |
| QUEUEFLOW_API_URL | Não, se URL sem credenciais | Runtime | SSR dos dados do display |
| NEXT_PUBLIC_QUEUEFLOW_API_URL | Não | Build-time | SignalR |
| NODE_ENV | Não | Build/runtime | production |

NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL pertence à configuração do Admin que monta o link.

## 5. E-mail e Production readiness

**Não existe provider real de e-mail na implementação atual.**

Contrato: src/QueueFlow.Application/Abstractions/Notifications/IActivationEmailSender.cs:
- IsConfigured;
- SendVerificationCodeAsync(email, code, cancellationToken);
- SupportsInvitationDelivery, default false;
- SendInvitationAsync(email, activationUrl, cancellationToken), opcional.

Implementação registrada: ActivationEmailSender, em Infrastructure/Notifications. É fake em Development/Test e retorna indisponibilidade em Production/Staging. Não usa SMTP, API externa ou entrega real.

Sem um adaptador real:
- não é possível entregar o código em Production;
- o destinatário de novo convite não consegue verificar o e-mail nem concluir legitimamente a ativação;
- “Enviar convite” não fica disponível;
- criar/reemitir/copiar link manualmente e consultar o painel platform continuam possíveis com configuração adequada;
- envio manual do link não substitui a entrega do código;
- logins de contas existentes não dependem desse adaptador.

**Impedimentos atuais de Production:** R1/R2/R3, demais inconsistências descritas, ausência de entrega real do código e ausência de validação do banco/configuração efetiva do Render. A ausência do provider já era uma dependência declarada; os achados de segurança são motivos independentes para reprovar o commit.

## 6. Checklist dos 74 pontos

Legenda: OK = confirmado no escopo indicado; PARCIAL = ressalva material; FALHA = achado; NÃO CONFIRMADO = falta evidência externa.

| Itens | Resultado | Evidência/ressalva |
| --- | --- | --- |
| 1–2 | OK | PlatformUser separado e sem OrganizationId |
| 3–6 | OK | Emissão e policies; regressões de JWT e adulteração |
| 7 | OK com exceções intencionais | Negócio + me protegidos; login/refresh públicos por necessidade do protocolo |
| 8 | FALHA | R1 permite usar o link pela rota anônima após verificação de outra pessoa |
| 9 | OK | SaveChanges síncrono/assíncrono rejeita escrita platform em ITenantEntity |
| 10 | OK no fluxo normal | IDs das requests não substituem tenant assinado; assinatura adulterada é rejeitada |
| 11 | FALHA fora da tabela | R3: token puro em logs; entidade guarda somente hash |
| 12 | OK | Banco de convites contém TokenHash; sem ciphertext reversível/token puro |
| 13 | FALHA | R3: aparição adicional em logs |
| 14–15 | OK | Reemissão invalida o original; single-use |
| 16–17 | OK estático | IsUsable verifica expiração/UsedAt/RevokedAt; lock nas operações |
| 18–20 | OK | Concorrência, transação e rollback por falha de banco testados |
| 21 | OK para código numérico | PostConfigure de Production + teste com sink; R3 é token de convite |
| 22 | OK com escopo | Cooldown/tentativas persistidos; rate limit de processo, não distribuído |
| 23–25 | OK | Register fechado em Production e sem outro bypass de criação irrestrita |
| 26–27 | OK | Login tenant e platform testados e separados |
| 28 | OK entre tenant/platform | Nomes distintos; observação Admin/Attendant acima |
| 29–30 | OK | Ambos os refresh cruzados retornaram 401 |
| 31 | PARCIAL | Tenant/platform independentes; Admin/Attendant apagam cookies um do outro no mesmo host |
| 32 | PARCIAL | AuthService + domínio normalizam; R5 e R7 |
| 33–34 | OK | Migration integral inspecionada; preflight precede índice |
| 35 | PARCIAL | Duplicidade canônica aborta; normalização SQL não é idêntica à .NET |
| 36 | OK no Up | Sem correção/exclusão automática |
| 37 | OK com risco documentado | Down destrói dados platform e preserva tenants ativados |
| 38 | Revisado | Locks descritos; duração real não estimada |
| 39 | NÃO CONFIRMADO | Sem conexão/evidência do PostgreSQL atual do Render |
| 40–44 | OK | CLI explícita, segura contra concorrência, senha no ambiente |
| 45 | OK no painel | Consultas globais/convites sem virar tenant; R1 compromete fluxo de ativação |
| 46–47 | OK com env correto | Redirecionamento Admin usa e-mail, sem secret; NEXT_PUBLIC_QUEUEFLOW_ADMIN_URL obrigatório |
| 48 | PARCIAL | Primeira entrada sem Branch vai ao onboarding; R6 |
| 49–50 | OK estático | Etapas e endpoints reais; equipe adicional é opcional na implementação atual |
| 51 | PARCIAL | Dados persistem, retomada automática deixa de ocorrer após primeira Branch |
| 52 | OK para caso completo | Login leva ao Dashboard; critério também aceita casos incompletos, R6 |
| 53 | PARCIAL | Consulta funciona; atualização automática incompleta, R4 |
| 54–57 | OK | Unidades atribuídas, check-in, IDs e prioridade temporal |
| 58 | PARCIAL | Filas passam no teste SignalR real; R2 e R4 em agendamentos |
| 59–60 | OK na central | Usa env e não gera localhost/onrender hardcoded; defaults em outras partes foram apontados |
| 61–63 | OK estático/rotas | /display/{branchPublicId}, /unidade/{branchPublicId}, /agendamento/{slug} |
| 64 | OK para link direto | Origem Attendant correta; handoff autenticado entre domínios não existe |
| 65–69 | Entregue | Matriz por app, secrets e build/runtime acima |
| 70 | Entregue | Bloqueadores listados acima |
| 71–74 | Entregue | Fake confirmado, contrato descrito, provider não implementado e fluxos bloqueados explicitados |

## 7. Validação executada novamente

| Validação | Resultado |
| --- | --- |
| dotnet build QueueFlow.sln --no-restore | Sucesso, zero avisos/erros |
| Todos os testes .NET | 135 aprovados: 38 domínio + 33 aplicação + 2 arquitetura + 62 integração; zero ignorados |
| Integração, concorrência, isolamento e migrations | Incluídos na suíte acima, executados com PostgreSQL real temporário |
| Preflight SQL no banco temporário principal | Zero duplicidades |
| Testes frontend (Vitest) | 17 aprovados |
| Builds Admin, Customer, Attendant e Display | Todos aprovados |
| Smoke HTTP platform | Aprovado: login, cookies, dashboard, criação, revogação 204 e rejeição de cookie tenant |
| SignalR real de filas | Aprovado: chegada, contagem, chamada, posição, conclusão, reconexão, isolamento, transições e check-in/outbox |
| Probes de segurança extras | Confirmaram R1, R2, R3, R4 e R5 |
| Comparação de normalização | Confirmou diferença SQL/.NET de R7 |

O projeto temporário de probes teve a entrada/content root ajustados para poder reutilizar o TestServer; isso não modificou a aplicação. A restauração de seus pacotes terminou, porém a consulta online de vulnerabilidades NuGet retornou NU1900 mesmo após nova tentativa fora do sandbox. Não foi feita afirmação de auditoria online de dependências concluída.

Não houve teste visual completo em navegador; os testes frontend executados foram Vitest, builds e smoke HTTP. A conectividade SignalR foi testada por cliente Node.

### Evidências locais

- .artifacts/final-review/test-results/*.trx
- .artifacts/final-review/dotnet-tests.log
- .artifacts/final-review/frontend-tests.log
- .artifacts/final-review/frontend-builds.log
- .artifacts/final-review/realtime-test.log
- .artifacts/final-review/probe-results.json
- .artifacts/final-review/source-changes.json

Os logs brutos dos probes contêm somente dados sintéticos, incluindo os tokens usados para demonstrar R3; não devem ser confundidos com logs/credenciais de produção.

## 8. Encaminhamento

Manter B) NÃO APROVADO PARA COMMIT até resolver os achados de segurança e as inconsistências apontadas. A primeira decisão é como vincular a verificação de e-mail à autorização para definir a senha e criar a organização.

Nenhum git commit, git push, deploy, migration no Render, PlatformAdmin real ou alteração de environment variables do Render foi executado.
