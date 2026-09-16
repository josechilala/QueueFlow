# Fechamento das Partes 1–4 — QueueFlow

**Resultado: APROVADO PARA COMMIT.**

Fechamento local validado em 16/09/2026. Preparação de Production continua sujeita às etapas externas descritas neste relatório.

Escopo: pacote local de Central de Links, agendamentos do atendente, administração global, convites, ativação e onboarding. Este documento substitui as conclusões da revisão anterior; os relatórios anteriores ficam como histórico dos achados. Não houve commit, push, deploy, acesso ou alteração no Render, criação de administrador/cliente real ou alteração de secrets de Production.

## 1. Arquitetura final

Domain contém entidades e invariantes, sem dependências de EF, HTTP, Next.js ou fornecedores. Application contém casos de uso e ports; Infrastructure implementa persistência PostgreSQL, hashing/JWT, notificações e SignalR; API compõe dependências, policies e contratos HTTP. Os testes de arquitetura continuam passando. O frontend consulta o backend para progresso e autorização; não decide a conclusão do onboarding por estado local.

## 2. Achados corrigidos

| Achado da revisão | Severidade | Correção e evidência |
| --- | --- | --- |
| R1: tomar ativação após outra pessoa verificar | Critical | Autorização aleatória independente, hash, dez minutos, vínculo ao convite e consumo atômico. Navegador sem autorização recebe 404; concorrência produz um único tenant. |
| R2: capability de agendamento em grupo público | High | Payload público construído por campos permitidos; nenhum objeto arbitrário do outbox é retransmitido. Testes com payload aninhado e assinatura SignalR real. |
| R3: token de convite nos logs | High | Links novos usam fragmento; frontend envia tokens em POST JSON. Redação de propriedades/scopes, supressão de logs de parâmetros do EF, exclusão de rotas públicas/hub do tracing HTTP e erros sem exceção bruta. Teste com sink em Production cobre token, autorização, código e senha. |
| R4: lista aberta do atendente desatualizada | Medium | API processa outbox de appointments; invalida grupos das filas relacionadas e do serviço. Attendant assina serviços autorizados, inclusive sem fila, e retoma assinaturas ao reconectar. Admin também atualiza agendamentos. |
| R5: email de outro tenant causava 500 | Medium | Consulta global de unicidade sem revelar dados do outro tenant; conflito 409, inclusive violação concorrente do índice no save assíncrono. |
| R6: onboarding abandonado após primeira unidade | Medium | Progresso calculado no backend; conclusão explícita persistida; login e Dashboard consultam esse estado. Teste retoma em novas sessões e rejeita conclusão antecipada. |
| R7: normalização SQL diferente da aplicação | Medium | SQL usa mapeamento Unicode invariant da mesma versão .NET e conjunto exato de whitespace do Trim. Preflight aborta duplicidades e dados não canônicos sem reescrevê-los; constraint exige email canônico. |
| Logout Admin apagava sessão Attendant e vice-versa | Medium | Cada frontend remove apenas seus cookies. Platform continua independente. |
| Redirects platform ignoravam origem pública | Medium | Refresh/logout usam publicUrl; smoke verifica origem configurada mesmo com forwarded-host diferente. |
| Fallbacks de URLs locais em Production | Medium | Helpers não aplicam defaults de desenvolvimento em Production. URLs públicas exigem HTTPS sem loopback; chamadas internas server-side aceitam URL explicitamente configurada. |
| BFF e resposta 204 | Low | Proxy tenant preserva corpo nulo; conclusão do onboarding validada por smoke HTTP. |

O login de Attendant pelo Admin encaminha ao login do aplicativo Attendant. As aplicações têm sessões independentes; não há SSO ou transferência de JWT por URL. Essa separação também funciona quando os hosts são diferentes.

## 3. Arquivos alterados

O inventário completo do pacote aparece no apêndice. Os grupos centrais são: entidades globais e Organization; serviços Platform, Onboarding, Auth e Users; controllers/policies; DbContext/migrations; notifier/outbox/observabilidade; páginas de ativação/onboarding/platform; sessões, proxies e helpers de URL; testes e scripts SQL/realtime. Alterações anteriores das Partes 1–4 foram preservadas. Evidências temporárias ficam em `.artifacts`, fora do pacote versionável.

## 4. Migrations

Ordem após `20260914061709_EnforceSingleActiveQueuePerService`:

1. `20260915060310_AddPlatformAdministrationAndInvitations`: tabelas globais, índices, função de normalização e preflight obrigatório sob lock de Users.
2. `20260915201718_SecureActivationAndOnboarding`: hash/expiração/consumo da autorização e `Organizations.OnboardingCompletedAt`.
3. `20260916010353_EnforceCanonicalUserEmail`: `CK_Users_CanonicalEmail`; com o índice global, impede duplicidade sob a normalização adotada.

Scripts `platform-administration.up.sql`, `.down.sql` e `preflight-global-user-email.sql` atualizados. O preflight independente é somente leitura e não exige que a função já exista no banco. A CLI `--preflight-user-email` também detecta duplicados e valores não canônicos, com exit code 2. Não exclui, normaliza ou escolhe usuários automaticamente.

## 5. Endpoints

| Área | Contratos principais |
| --- | --- |
| Tenant auth | `/api/v1/auth/login`, `/refresh`, `/me`; register fechado em Production |
| Platform auth | `/api/v1/platform/auth/login`, `/refresh`, `/me` |
| Platform | `/api/v1/platform/dashboard`, `/organizations`, `/subscriptions`, `/audit`, `/invitations` e ações revoke/reissue/send |
| Ativação sem token na rota | POST `/api/v1/public/activation/lookup`, `/request-code`, `/verify`, `/complete` |
| Onboarding Owner | GET `/api/v1/onboarding`; POST `/api/v1/onboarding/complete` |
| Operação | GET `/api/v1/operations/context`, `/appointments/today`; POST `/appointments/{id}/check-in` |

Verify recebe `invitationToken` e `code`; responde `activationAuthorization` e `expiresAt`. Complete recebe `invitationToken` e objeto `activation` com autorização, empresa, responsável, senha e timezone. As rotas de ativação anteriores com token no path permanecem compatíveis e protegidas por redação; novos links/frontend usam fragmento e corpo. Respostas de autenticação/ativação/platform usam no-store.

## 6. Entidades

Globais: PlatformUser, PlatformRefreshToken, OrganizationInvitation e PlatformAuditLog, sem OrganizationId tenant. A autorização é persistida como hash e metadados na própria Invitation, vinculada pela PK e protegida pelo mesmo lock. Entidades operacionais existentes continuam Organization, Branch, Service, Queue, ServiceSchedule, ServiceSchedulingSettings, AppUser, Subscription, Appointment e QueueTicket. Não foi criado domínio paralelo de onboarding.

## 7. Policies

TenantIdentity, AdminPanel, UserManagement, AttendantPanel, OrganizationManagement, SubscriptionManagement, ReportRead e AuditRead exigem identidade tenant e organização válida. RequirePlatformAdmin exige identidade platform/role PlatformAdmin, sem organização. As permissões de filial continuam validadas no backend. A escrita ITenantEntity por identidade platform falha no DbContext.

## 8. JWT e sessões

Tenant JWT contém identity_type=tenant e organization_id; platform contém identity_type=platform, sem organização. Refresh stores, endpoints e cookies são separados. Testes cobrem JWT cruzado, assinatura adulterada, organização forjada e bloqueio de escrita. PlatformAdmin não é UserRole tenant e não há impersonation. Login com duplicidade exata legada retorna credenciais inválidas, evitando SingleOrDefault lançar 500.

## 9. Invitation

Token de 256 bits, apenas hash persistido, expiração, revogação e uso único. Create/Reissue retornam o link uma vez; listagens não recuperam token. Reissue invalida o anterior. Link novo: `https://CUSTOMER/ativar#TOKEN`; o navegador retira o fragmento do histórico após capturá-lo em memória. Recarregar exige reabrir o link recebido; nenhum secret é guardado em localStorage/sessionStorage.

## 10. ActivationAuthorization e atomicidade

Confirmação correta do código emite autorização independente de 256 bits, válida por dez minutos e nunca em URL. O código é invalidado ao verificar; solicitar novo código invalida a autorização anterior. Complete exige token do convite e autorização compatível/não expirada/não consumida, com comparação de hash em tempo constante. VerifiedAt não autoriza ativação.

Uma transação com lock de linha engloba Organization, Owner, Trial, consumo da autorização, UsedAt e auditoria. Falha injetada no banco reverte tudo. Seis solicitações concorrentes produzem exatamente um Organization, um Owner e um Trial. A autorização só fica no estado transitório do cliente verificado e não é devolvida em consulta posterior.

## 11. PlatformAdmin e bootstrap

Painel global oferece empresas, convites, subscriptions/trials e auditoria. Bootstrap é CLI explícita, one-time, com advisory lock, PasswordService e senha pelo ambiente. Não executa no startup. Comando futuro, somente após autorização operacional e configuração temporária dos secrets:

```sh
dotnet QueueFlow.Api.dll --provision-platform-admin
```

Remover a senha e desabilitar PlatformBootstrap__Enabled após provisionar. Não foi executado em Production.

## 12. Onboarding

Empresa já cadastrada na ativação → unidade → serviços → fila/agenda → equipe → links → conclusão. A equipe adicional permanece opcional conforme a arquitetura existente: Owner pode começar atendendo. QueueOnly exige fila; AppointmentOnly exige agenda ativa com horários; Hybrid exige ambos. O backend considera apenas serviços vinculados a unidades ativas do tenant. Login retorna ao onboarding enquanto não concluído; Dashboard também verifica. A conclusão persistida não desaparece por mudanças operacionais posteriores.

## 13. Appointments

Hoje/confirmar chegada respeitam UserBranches, Organization, Branch e Service. Check-in continua idempotente sob concorrência e cria ticket na fila correta. ScheduledStart continua sendo a referência de elegibilidade temporal; chegada antecipada não antecipa a chamada. Walk-ins, cancelamento, no-show e transições continuam cobertos pelos testes existentes.

## 14. SignalR

Eventos públicos de fila/serviço carregam somente invalidação ou projeção explícita de chamada (senha, guichê, status e IDs necessários). Payloads privados permanecem nos grupos que exigem conhecimento do token correspondente. Cliente anônimo de serviço não recebe capacidade de consultar/cancelar outro appointment. API processa eventos duráveis de fila e agendamento; backplane Redis continua necessário quando conexões/eventos atravessam instâncias.

## 15. Central de Links

Mantém Attendant, Display, atendimento presencial, QR e agendamento por empresa/unidade. Helpers centralizam origens e normalizam barras; links públicos inválidos ou ausentes não ganham fallback local em Production. Configurações NEXT_PUBLIC são públicas e incorporadas no build. A central respeita as permissões da interface, enquanto o backend segue responsável pela autorização.

## 16–19. Testes, builds, migrations e revisão adversarial

| Validação | Resultado |
| --- | --- |
| .NET build da solução | Zero warnings, zero errors |
| Suíte .NET completa | 151 aprovados: 38 Domain, 33 Application, 2 Architecture, 78 Integration; nenhum ignorado |
| Frontend Vitest | 20 aprovados |
| Builds web | Admin, Customer, Attendant e Display aprovados; Admin/Customer reconstruídos após ajustes finais |
| Smoke HTTP Next | Login platform, cookies, dashboard, criar/revogar convite, isolamento tenant/platform, redirect público, onboarding retomável e 204 |
| SignalR real, múltiplos clientes | Chegadas, contagem, posição, chamada/guichê, conclusão, recall, reconexão, isolamento, criação de appointment sem capability pública e check-in/outbox sem Redis |
| Migration/preflight/rollback | Banco temporário PostgreSQL 17; vazio e schema anterior; Up/Down/Up; duplicados e não canônicos abortam sem reescrita; Unicode SQL/.NET equivalente nos casos exercitados |
| Preflight do banco temporário principal | Zero duplicidades e zero emails não canônicos |
| Diff | `git diff --check` aprovado, exit code 0; arquivos novos também conferidos quanto a whitespace |

Cobertura adversarial permanente:

- SEC-01–06: ausência, validade, reutilização, expiração, convite incorreto e concorrência da autorização.
- SEC-07–08: secrets aninhados eliminados de eventos públicos; assinatura real do serviço sem token e tentativa de cancelamento com identificador público rejeitada.
- SEC-09–11: sink Production sem convite, autorização, código ou senha; nenhuma exposição de código em response Production.
- SEC-12–16: JWT cruzado, ITenantEntity, organização forjada e register Production fechado.
- SEC-17–20: reissue, Used, Revoked e Expired não reutilizáveis.
- Adicionais: traversal tenant/branch, check-in concorrente, fila ativa única, email global duplicado, rollback de ativação e conclusão antecipada do onboarding.

Os testes funcionais exercitam componentes do fluxo completo por HTTP/integração e clientes SignalR reais. Não foi executada uma sessão visual completa em navegador automatizado; não se afirma cobertura visual de layout. A auditoria online de dependências não foi reexecutada neste fechamento.

Após o ajuste final de escape Unicode no SQL gerado, os seis testes de migration/preflight/rollback foram repetidos e passaram. Uma tentativa anterior sofreu reset de conexão local durante abertura do banco, antes da consulta; a repetição concluiu sem falhas. A suíte completa de 151 testes passou antes desse ajuste de representação SQL, e o build final voltou a concluir sem warnings/errors.

Evidências locais: `.artifacts/closure-build.log`, `closure-tests.log`, `closure-migration-tests.log`, `closure-frontend-tests.log`, `closure-frontend-builds.log`, `closure-admin-final-build.log`, `closure-customer-final-build.log`, `closure-platform-smoke.log` e `.artifacts/closure/{test-results,realtime-test.log,preflight.log,diff-check.log}`.

## 20–22. Environment, secrets e futura configuração do Render

A matriz completa por aplicação está abaixo. Nenhuma variável do Render foi consultada/alterada. O schema, versão/collation, histórico de migrations, volume, locks e permissões do banco real ainda precisam ser conferidos antes da preparação de Production. Isso é uma etapa externa de preparação, não uma falha do pacote local.

## 23. Email provider

**Production integration pending.** IActivationEmailSender permanece o port na Application, com adapter fake apenas em Development/Test. Production falha fechado com 503 quando não há provider real; nunca retorna código como fallback. A ausência do provider não bloqueia o commit, conforme o objetivo de fechamento, mas bloqueia a ativação de clientes reais até implementação, configuração e teste de entrega.

## 24. Riscos residuais e limites

- Aplicar migrations requer janela/avaliação de locks; preflight/índice global bloqueiam escrita de Users. Não foi medido volume equivalente ao Render.
- Down remove dados platform e autorizações/progresso; não desfaz tenants já ativados. Exige backup e plano de rollback operacional.
- Rate limits atuais são por processo. Antes de múltiplas instâncias, dimensionar limites e backplane conforme a topologia.
- Ao atualizar runtime/Unicode, repetir testes de normalização contra a função SQL persistida.
- URLs antigas com secrets no path exigem manter redação também no proxy de hospedagem. Novos convites eliminam esse transporte no frontend.
- Não há vulnerabilidade Critical/High conhecida remanescente no escopo exercitado. Isso não equivale a garantia contra vulnerabilidades futuras ou configuração externa incorreta.

## 25. Ordem futura de preparação/deploy

1. Revisar este pacote e efetuar commit somente mediante comando do usuário.
2. Conferir banco real somente leitura, histórico/schema, preflight, capacidade e permissões; resolver duplicidades por decisão explícita, nunca automaticamente.
3. Preparar backup restaurável, janela de manutenção e plano de rollback.
4. Implementar/testar provider real e configurar secrets/runtime e URLs de build por aplicação.
5. Aplicar migrations em staging representativo; repetir smoke, entrega de email e multi-instância.
6. Após autorização de Production: migrations antes de iniciar a API nova; publicar API/Worker e frontends compatíveis.
7. Executar bootstrap one-time aprovado, remover seus secrets temporários, validar isolamento e ativação real controlada.

Nenhuma dessas ações externas foi realizada neste fechamento.

## Apêndice A. Variáveis para o Render, por aplicação

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
| OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_PROTOCOL | Existentes, opcionais | Conforme configuração | Telemetria; rotas públicas sensíveis excluídas da instrumentação HTTP |

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


## Apêndice B. Inventário do pacote local

Inclui as alterações anteriores das Partes 1–4 e as correções deste fechamento. Arquivos removidos permanecem identificáveis pelo Git.

```text
.env.example
.env.staging.example
.gitignore
Directory.Packages.props
apps/admin-web/Dockerfile
apps/admin-web/app/api/auth/login/route.ts
apps/admin-web/app/api/onboarding/complete/route.ts
apps/admin-web/app/api/platform/auth/login/route.ts
apps/admin-web/app/api/platform/auth/logout/route.ts
apps/admin-web/app/api/platform/auth/refresh/route.ts
apps/admin-web/app/api/platform/invitations/[id]/[action]/route.ts
apps/admin-web/app/api/platform/invitations/route.ts
apps/admin-web/app/appointments/[id]/appointment-actions.tsx
apps/admin-web/app/appointments/[id]/page.tsx
apps/admin-web/app/appointments/page.tsx
apps/admin-web/app/branches/[id]/page.tsx
apps/admin-web/app/dashboard/page.tsx
apps/admin-web/app/dashboard/public-scheduling-link.tsx
apps/admin-web/app/login/login-form.tsx
apps/admin-web/app/onboarding/complete-button.tsx
apps/admin-web/app/onboarding/page.tsx
apps/admin-web/app/platform/(protected)/audit/page.tsx
apps/admin-web/app/platform/(protected)/invitations/invitation-manager.tsx
apps/admin-web/app/platform/(protected)/invitations/page.tsx
apps/admin-web/app/platform/(protected)/layout.tsx
apps/admin-web/app/platform/(protected)/organizations/page.tsx
apps/admin-web/app/platform/(protected)/page.tsx
apps/admin-web/app/platform/(protected)/subscriptions/page.tsx
apps/admin-web/app/platform/login/page.tsx
apps/admin-web/app/platform/login/platform-login-form.tsx
apps/admin-web/app/queues/[id]/page.tsx
apps/admin-web/app/reports/page.tsx
apps/admin-web/app/services/[id]/scheduling-config-form.tsx
apps/admin-web/app/styles.css
apps/admin-web/components/access-links.tsx
apps/admin-web/components/platform-shell.tsx
apps/admin-web/components/realtime-refresh.tsx
apps/admin-web/lib/access-links.test.ts
apps/admin-web/lib/access-links.ts
apps/admin-web/lib/auth-cookies.ts
apps/admin-web/lib/auth.ts
apps/admin-web/lib/configured-url.ts
apps/admin-web/lib/onboarding.test.ts
apps/admin-web/lib/onboarding.ts
apps/admin-web/lib/platform-auth-cookies.ts
apps/admin-web/lib/platform-auth.ts
apps/admin-web/lib/public-scheduling.test.ts
apps/admin-web/lib/public-scheduling.ts
apps/admin-web/lib/server-access-links.ts
apps/admin-web/lib/server-api-proxy.ts
apps/admin-web/lib/server-onboarding.ts
apps/admin-web/lib/server-platform-proxy.ts
apps/admin-web/lib/server-platform.ts
apps/admin-web/lib/session-security.test.ts
apps/admin-web/package.json
apps/admin-web/proxy.ts
apps/admin-web/tests/access-links.mjs
apps/admin-web/tests/platform-smoke.mjs
apps/attendant-web/app/api/appointments/[id]/check-in/route.ts
apps/attendant-web/app/styles.css
apps/attendant-web/app/workstation/page.tsx
apps/attendant-web/app/workstation/realtime-refresh.tsx
apps/attendant-web/app/workstation/workstation.tsx
apps/attendant-web/lib/auth-cookies.ts
apps/attendant-web/lib/auth.ts
apps/attendant-web/lib/configured-url.ts
apps/attendant-web/lib/server-data.ts
apps/customer-web/Dockerfile
apps/customer-web/app/agendar/[branchPublicId]/page.tsx
apps/customer-web/app/api/appointments/[publicToken]/[action]/route.ts
apps/customer-web/app/api/appointments/route.ts
apps/customer-web/app/api/queues/[publicId]/tickets/route.ts
apps/customer-web/app/appointments/[publicToken]/page.tsx
apps/customer-web/app/ativar/[token]/activation-flow.tsx
apps/customer-web/app/ativar/[token]/page.tsx
apps/customer-web/app/ativar/page.tsx
apps/customer-web/app/empresa/[slug]/page.tsx
apps/customer-web/app/q/[publicId]/page.tsx
apps/customer-web/app/realtime-refresh.tsx
apps/customer-web/app/ticket/[token]/page.tsx
apps/customer-web/app/unidade/[publicId]/page.tsx
apps/customer-web/components/availability-view.tsx
apps/customer-web/lib/configured-url.ts
apps/customer-web/lib/public-scheduling.ts
apps/display-web/app/display/[branchPublicId]/page.tsx
apps/display-web/app/q/[publicId]/page.tsx
apps/display-web/lib/configured-url.ts
apps/display-web/lib/use-queue-display.ts
docker-compose.staging.yml
docker-compose.yml
docs/closure-parts-1-4.md
docs/final-review-parts-1-4.md
docs/platform-administration-and-onboarding.md
scripts/platform-administration.down.sql
scripts/platform-administration.up.sql
scripts/preflight-global-user-email.sql
scripts/test-queue-realtime.mjs
src/QueueFlow.Api/Controllers/AuthController.cs
src/QueueFlow.Api/Controllers/OnboardingController.cs
src/QueueFlow.Api/Controllers/OperationsController.cs
src/QueueFlow.Api/Controllers/PlatformAuthController.cs
src/QueueFlow.Api/Controllers/PlatformController.cs
src/QueueFlow.Api/Controllers/PublicActivationController.cs
src/QueueFlow.Api/Controllers/UsersController.cs
src/QueueFlow.Api/Middleware/ExceptionHandlingMiddleware.cs
src/QueueFlow.Api/Middleware/SafeRequestLogEnricher.cs
src/QueueFlow.Api/Program.cs
src/QueueFlow.Api/appsettings.Development.json
src/QueueFlow.Api/appsettings.json
src/QueueFlow.Application/Abstractions/Authentication/AuthenticationAbstractions.cs
src/QueueFlow.Application/Abstractions/Notifications/IActivationEmailSender.cs
src/QueueFlow.Application/Abstractions/Persistence/IApplicationDbContext.cs
src/QueueFlow.Application/Common/ConflictException.cs
src/QueueFlow.Application/Features/Appointments/AppointmentManagementService.cs
src/QueueFlow.Application/Features/Appointments/OperationalAppointmentService.cs
src/QueueFlow.Application/Features/Auth/AuthService.cs
src/QueueFlow.Application/Features/Platform/OnboardingOptions.cs
src/QueueFlow.Application/Features/Platform/OrganizationInvitationService.cs
src/QueueFlow.Application/Features/Platform/PlatformAdministrationService.cs
src/QueueFlow.Application/Features/Platform/PlatformAuthService.cs
src/QueueFlow.Application/Features/Platform/PlatformBootstrapService.cs
src/QueueFlow.Application/Features/Queues/QueueOperationsService.cs
src/QueueFlow.Application/Features/Tenants/OnboardingService.cs
src/QueueFlow.Application/Features/Tenants/TenantService.cs
src/QueueFlow.Application/Features/Users/UserManagementService.cs
src/QueueFlow.Application/QueueFlow.Application.csproj
src/QueueFlow.Domain/Entities/PlatformEntities.cs
src/QueueFlow.Domain/Entities/TenantEntities.cs
src/QueueFlow.Domain/Enums/DomainEnums.cs
src/QueueFlow.Infrastructure/Authentication/CurrentUser.cs
src/QueueFlow.Infrastructure/Authentication/JwtTokenService.cs
src/QueueFlow.Infrastructure/DependencyInjection.cs
src/QueueFlow.Infrastructure/Jobs/OutboxProcessor.cs
src/QueueFlow.Infrastructure/Notifications/ActivationEmailSender.cs
src/QueueFlow.Infrastructure/Observability/ObservabilityExtensions.cs
src/QueueFlow.Infrastructure/Persistence/ApplicationDbContext.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260915060310_AddPlatformAdministrationAndInvitations.Designer.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260915060310_AddPlatformAdministrationAndInvitations.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260915201718_SecureActivationAndOnboarding.Designer.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260915201718_SecureActivationAndOnboarding.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260916010353_EnforceCanonicalUserEmail.Designer.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/20260916010353_EnforceCanonicalUserEmail.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
src/QueueFlow.Infrastructure/Persistence/Migrations/InvariantEmailNormalization.cs
src/QueueFlow.Infrastructure/Realtime/SignalRQueueRealtimeNotifier.cs
tests/QueueFlow.IntegrationTests/Appointments/OperationalAppointmentEndpointTests.cs
tests/QueueFlow.IntegrationTests/Health/LiveHealthEndpointTests.cs
tests/QueueFlow.IntegrationTests/Persistence/TicketIssuanceConcurrencyTests.cs
tests/QueueFlow.IntegrationTests/Platform/InvitationFlowTests.cs
tests/QueueFlow.IntegrationTests/Platform/OnboardingResumeTests.cs
tests/QueueFlow.IntegrationTests/Platform/PlatformLifecycleTests.cs
tests/QueueFlow.IntegrationTests/Platform/PlatformMigrationTests.cs
tests/QueueFlow.IntegrationTests/Platform/PlatformSecurityTests.cs
tests/QueueFlow.IntegrationTests/Platform/RealtimePrivacyTests.cs
```
