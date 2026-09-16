# Partes 3 e 4 — administração da plataforma e ativação

> Documento histórico da implementação inicial. Para contratos, migrations e estado atual, consulte [o relatório de fechamento](closure-parts-1-4.md).
Data: 15/09/2026.

## Escopo e resultado

Implementação local de Platform Admin, convites, verificação de e-mail, ativação atômica e onboarding. Este relatório usa as 20 decisões do anexo aprovado; o texto integral do “prompt mestre” não estava disponível nesta retomada.

Nenhum push, deploy, migration no Render, provisionamento de administrador real, convite real ou alteração de secrets de produção foi realizado. Contas e convites criados nos testes são sintéticos, em bancos PostgreSQL temporários isolados.

A regra de prioridade temporal da Parte 2 foi preservada. Seus testes de política híbrida, concorrência e agendamento participaram da suíte completa.

## Arquitetura entregue

- PlatformUser, PlatformRefreshToken, PlatformAuditLog e OrganizationInvitation são entidades globais. PlatformUser não implementa ITenantEntity e não possui OrganizationId.
- PlatformAdmin é uma role do JWT platform, não um valor adicional de UserRole/AppUser.
- Tenant JWT: identity_type=tenant, organization_id e role tenant.
- Platform JWT: identity_type=platform, role PlatformAdmin, sem organization_id.
- Todas as oito policies tenant exigem identidade tenant, uma claim de organização GUID válida e não vazia e uma única claim de cada tipo.
- RequirePlatformAdmin exige identidade platform e rejeita organization_id.
- CurrentUser distingue UserId tenant de PlatformUserId.
- ApplicationDbContext rejeita escrita tenant por identidade platform, mesmo se ela carregar OrganizationId. Identidade autenticada sem contexto tenant válido também falha. Escritas públicas legítimas continuam em serviços específicos de ativação/fila; os endpoints tenant continuam protegidos.
- Não há impersonation, troca de organização da identidade platform ou sessão silenciosa de Owner.

### Rotas

| Função | Interface | API |
| --- | --- | --- |
| Login tenant | /login | POST /api/v1/auth/login |
| Login platform | /platform/login | POST /api/v1/platform/auth/login |
| Sessão platform | painel protegido | GET /api/v1/platform/auth/me; POST /refresh |
| Dashboard global | /platform | GET /api/v1/platform/dashboard |
| Empresas | /platform/organizations | GET /api/v1/platform/organizations |
| Assinaturas | /platform/subscriptions | GET /api/v1/platform/subscriptions |
| Auditoria | /platform/audit | GET /api/v1/platform/audit |
| Convites | /platform/invitations | GET/POST /api/v1/platform/invitations |
| Ações do convite | criação/reemissão | POST /invitations/{id}/revoke, /reissue, /send |
| Ativação | /ativar/{token} no Customer | /api/v1/public/activation/{token}, /verification-code, /verify, /complete |
| Configuração inicial | /onboarding | reutiliza branches/services/queues/users/scheduling existentes |

### Sessões e interface

Os cookies platform se chamam queueflow_platform_access e queueflow_platform_refresh, separados dos cookies tenant. Usam HttpOnly, SameSite=Lax e Secure em produção, salvo override explícito de ambiente. Path=/ permite recebê-los tanto em /platform quanto em /api/platform. A separação de autorização depende da identidade assinada e dos nomes de sessão.

O login platform fica fora do layout protegido, evitando redirecionamento circular. A expiração usa o endpoint de refresh separado.

Após ativação, o cliente segue para o Admin /login com o e-mail real pré-preenchido e activated=1. Esse redirecionamento não contém senha, JWT, refresh token, token de convite ou código. O usuário informa sua senha; um Owner sem unidade ativa segue para /onboarding.

Onboarding deriva o progresso de unidades e serviços ativos, filas, configurações/horários de agenda e equipe. QueueOnly exige fila; AppointmentOnly exige agenda ativa com horários; Hybrid exige ambos. Exibe links de acesso e permite concluir para o Dashboard. Há um acesso para retomar a configuração no Dashboard.

## Convites e verificação

- Token aleatório de 32 bytes (256 bits), representado por 64 caracteres hexadecimais.
- Somente o SHA-256 do token é persistido, com índice único.
- O link puro é exibido na criação/reemissão e fica somente no estado transitório da tela.
- A listagem não devolve TokenHash nem token.
- Reemitir revoga o anterior e cria outro registro com novo token e expiração.
- Estados: Pending, Verified, Used, Expired, Revoked.
- Reemissão de Used ou Revoked é rejeitada; para um convite revogado, crie outro convite.
- Revogação, reemissão, emissão de código, verificação e ativação usam bloqueio da linha correspondente.
- O código tem seis dígitos, geração criptográfica, HMAC-SHA256 vinculado ao ID do convite e pepper externo.
- Padrões: convite 24 horas; código 10 minutos; cooldown 60 segundos; cinco tentativas; revogação ao esgotar tentativas.
- Reenvio substitui o hash anterior e remove a verificação anterior.
- Rate limiting público e de autenticação continua ativo.
- Production/Staging forçam a exposição do código para false, mesmo que a flag de desenvolvimento seja habilitada indevidamente.

### Transação de ativação

BEGIN → SELECT Invitation FOR UPDATE → validar disponibilidade e verificação → preparar Organization, Owner e Trial → marcar UsedAt/ActivatedOrganizationId → SaveChanges → COMMIT.

Qualquer falha reverte a transação. Um teste injeta falha de banco ao atualizar UsedAt e comprova ausência de Organization, Owner e Subscription e convite ainda não utilizado. Outro dispara seis ativações simultâneas: uma tem sucesso e as demais são rejeitadas.

O refresh platform também bloqueia o token durante rotação, impedindo dois usos concorrentes.

## E-mail: dependência explícita para Production

IActivationEmailSender define a entrega de código e a capacidade opcional SupportsInvitationDelivery/SendInvitationAsync.

O adaptador incluído é fake apenas em Development/Test. Ele não registra nem entrega mensagens reais. Em Production, informa indisponibilidade. Não foi escolhido fornecedor.

Antes de liberar ativação em Production:

1. Implementar e registrar um adaptador real para IActivationEmailSender.
2. Configurar credenciais por environment/secret.
3. Configurar Onboarding__VerificationCodePepper com segredo forte externo e estável entre instâncias.
4. Validar entrega real, limites, timeout e tratamento de indisponibilidade.
5. Se suportar entrega de convites, habilitar SupportsInvitationDelivery. A interface mostrará “Enviar convite” somente nessa condição.

Enviar convite recebe o token que ainda está na tela, valida seu hash e o ID, e monta o link com a origem configurada. Não recupera token do banco. Sem entrega de convites, o administrador pode copiar o link e enviá-lo manualmente. Isso não substitui o canal real exigido para o código.

A configuração Development não contém pepper fixo. Defina Onboarding__VerificationCodePepper localmente antes de testar o fluxo manual. A flag Onboarding__ExposeVerificationCodeForDevelopment só funciona em Development/Test.

Links de ativação são credenciais bearer. Na configuração futura do proxy/observabilidade, evite registrar seus valores e não os envie a ferramentas de analytics.

## Provisionamento inicial

Somente CLI explícita: --provision-platform-admin. Não roda automaticamente no startup e não há endpoint público de cadastro platform.

- Requer PlatformBootstrap__Enabled=true.
- Nome/e-mail: PlatformBootstrap__Name e PlatformBootstrap__Email.
- Senha: exclusivamente PlatformBootstrap__Password no ambiente; mínimo de 12 caracteres.
- Usa PasswordService/ASP.NET Identity PasswordHasher.
- Lock transacional advisory PostgreSQL 710041903 serializa execuções.
- Repetir o mesmo e-mail não altera a senha nem cria usuário.
- Já existir outro administrador causa falha explícita.
- Nenhuma organização/usuário tenant é criada.

### Comando futuro no Render — documentado, não executado

Depois de configurar temporariamente as variáveis acima via secrets e aplicar a migration aprovada:

```sh
dotnet QueueFlow.Api.dll --provision-platform-admin
```

Depois do provisionamento, remover PlatformBootstrap__Password e desabilitar/remover PlatformBootstrap__Enabled. Não incluir senha como argumento de CLI, em URL ou em arquivo versionado.

## Migration e SQL

Migration: 20260915060310_AddPlatformAdministrationAndInvitations.

Arquivos:
- scripts/preflight-global-user-email.sql
- scripts/platform-administration.up.sql
- scripts/platform-administration.down.sql

### Preflight

A CLI --preflight-user-email usa exatamente Email.Trim().ToLowerInvariant(), como o login tenant, e lista Email, Count e Organizations. Retorna exit code 2 se houver duplicidades; zero caso contrário. Não altera registros.

```sh
dotnet QueueFlow.Api.dll --preflight-user-email
```

O SQL é uma auditoria adicional com lower(btrim("Email")); para caracteres Unicode atípicos, a CLI é a referência de normalização .NET. Execute ambos antes da alteração. A própria migration repete a proteção SQL e falha com mensagem explícita quando há duplicidade.

O preflight inicial do banco temporário principal retornou zero linhas. O teste negativo criou deliberadamente dois usuários com o mesmo e-mail em organizações distintas, em outro banco descartável. A migration falhou, preservou os dois usuários e não criou PlatformUsers. Esses dados sintéticos não foram resolvidos escolhendo/excluindo/renomeando usuários.

Não foi feita auditoria no banco real de produção ou alteração do banco local habitual do projeto.

### Locks, índices e constraints

- Transação única da migration e lock de controle do EF em __EFMigrationsHistory.
- LOCK TABLE Users IN SHARE ROW EXCLUSIVE MODE antes do preflight interno: impede escritas concorrentes durante auditoria e alteração do índice.
- DDL pode exigir locks adicionais; prever janela de manutenção e evitar transações longas.
- IX_Users_OrganizationId_Email é substituído por IX_Users_Email único global.
- PlatformUsers: PK e Email único.
- PlatformRefreshTokens: PK, TokenHash único e índice PlatformUserId/ExpiresAt.
- OrganizationInvitations: PK, TokenHash único, Email/ExpiresAt e ActivatedOrganizationId único filtrado para não nulos.
- PlatformAuditLogs: PK e índices CreatedAt e PlatformUserId/CreatedAt.
- Limites de tamanho das colunas textuais seguem o modelo.
- A relação Invitation → Organization é registrada pelo ID; a atomicidade é controlada pela transação e pelo lock de convite. A migration não adiciona foreign keys entre essas tabelas.

### Risco e rollback

A criação do índice global exige leitura dos usuários e bloqueia escritas durante a transação. O tempo depende do volume e das transações concorrentes; testes temporários não estimam duração em produção.

O SQL down remove as quatro tabelas platform e recria a unicidade tenant antiga. É destrutivo para convites, tokens platform, administradores e auditoria platform. Não remove Organizations/Owners já ativados. Exige backup e decisão explícita antes de qualquer uso real.

Up → Down → Up foi validado em banco temporário, preservando usuários tenant e reaplicando a unicidade global.

### Ordem futura de produção

1. Backup e ensaio em cópia com volume representativo.
2. Rodar preflight .NET e SQL; parar e relatar qualquer duplicidade.
3. Preparar canal de e-mail, pepper e URLs/secrets, sem expor códigos.
4. Pausar escritas e drenar transações; revisar locks/tempo de manutenção.
5. Aplicar a migration/SQL up uma única vez.
6. Publicar versões compatíveis da API e frontends.
7. Invalidar/renovar sessões tenant antigas sem identity_type.
8. Provisionar o primeiro PlatformAdmin via CLI one-time e remover o secret.
9. Validar isolamento, login, convite e ativação com autorização operacional própria.
10. Retomar tráfego e acompanhar falhas e entrega de e-mail.

Nenhuma dessas ações futuras foi executada no Render.

## Validação realizada

- Solução .NET: 38 testes de domínio, 33 de aplicação, 2 de arquitetura e 61 de integração aprovados; zero ignorados.
- Após os ajustes finais, os 32 testes de plataforma foram reexecutados com sucesso. Um teste adicional de adulteração de organization_id no JWT também passou: assinatura inválida retorna 401. Total de casos .NET validados: 135.
- Frontend: 17 testes Vitest aprovados, incluindo readiness de QueueOnly/AppointmentOnly/Hybrid.
- Builds Next.js Admin e Customer aprovados.
- TypeScript do Attendant verificado.
- Smoke HTTP do Admin: login platform público, cookies platform, dashboard, criação, revogação 204 e rejeição de cookie tenant.
- PostgreSQL 17 temporário em loopback, porta 55439; base principal queueflow_platform_test e bases isoladas com prefixo queueflow_platform_test_.
- Relatórios TRX locais em .artifacts/test-results (não versionados).

Testes novos cobrem policies tenant, ambos os sentidos de rejeição de JWT, platform sem organização, escrita tenant proibida, OrganizationId inválido/externo, armazenamento somente de hash, reemissão, concorrência de ativação, rollback por falha do banco, cooldown/reenvio/tentativas, Production sem exposição de código nos logs/resposta, register fechado, unicidade global, provisionamento concorrente/idempotente e rotação concorrente de refresh.

O smoke HTTP usa uma API simulada; não substitui teste visual em navegador. O fluxo de backend foi testado com PostgreSQL real temporário.

### Reproduzir localmente

Use um PostgreSQL descartável próprio, em 127.0.0.1, com nome de banco iniciado por queueflow_platform_test. Os testes de migration criam bases adicionais com esse prefixo; não apontar para dados reais.

```powershell
$env:QUEUEFLOW_PLATFORM_TEST_DATABASE = 'Host=127.0.0.1;Port=55439;Database=queueflow_platform_test;Username=postgres'
$env:ConnectionStrings__QueueFlowDatabase = $env:QUEUEFLOW_PLATFORM_TEST_DATABASE
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet test QueueFlow.sln --no-restore
npm.cmd run test
npm.cmd run build --workspace @queueflow/admin-web
node apps/admin-web/tests/platform-smoke.mjs
npm.cmd run build --workspace @queueflow/customer-web
```

O preflight .NET pós-testes retornou [] no banco temporário principal. Os bancos negativos com duplicidades deliberadas foram preservados no diretório temporário para inspeção, sem corrigir ou excluir seus usuários.

## Decisões de implementação explicitadas

1. Onboarding é uma página de etapas ligada aos cadastros existentes, com progresso derivado; não adiciona modelos paralelos ou estado de onboarding ao banco.
2. O Owner pode concluir sozinho e adicionar equipe depois; não exigimos criar uma segunda pessoa para operar.
3. Novas ativações usam o Trial existente de 14 dias. A interface não oferece planos pagos que a ativação não provisiona.
4. Reemissão cria novo registro e revoga o anterior, preservando histórico.
5. Cookies platform usam Path=/ por necessidade das rotas /api/platform, mantendo nomes e autorização separados.
6. Nenhum armazenamento reversível de token foi introduzido.

## Pendência de produção

O canal real de e-mail ainda precisa ser implementado/configurado antes de considerar a ativação pronta para Production. A entrega local contempla explicitamente essa dependência e falha fechada quando ela não está disponível.
