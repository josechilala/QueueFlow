# QueueFlow

Fundação backend do QueueFlow, uma plataforma SaaS multi-tenant de filas digitais. A especificação normativa é [ESPECIFICACAO_QUEUEFLOW.md](ESPECIFICACAO_QUEUEFLOW.md).

## Pré-requisitos

- .NET SDK 10.0.400
- Docker com Compose (opcional para executar PostgreSQL e a API em containers)

## Validação local

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

## Execução

Defina `ConnectionStrings__QueueFlowDatabase` fora do repositório e execute:

```powershell
dotnet run --project src/QueueFlow.Api
```

Com Docker disponível, execute `docker compose up`. O endpoint `/health/live` verifica o processo; `/health` também verifica PostgreSQL e Redis.

A migration inicial foi criada na Fase 1 junto às primeiras entidades e deve ser aplicada somente por uma etapa controlada de deployment.

## Banco e segurança

A migration inicial está versionada, mas nunca é executada automaticamente pela API. Aplique-a somente em uma etapa controlada de deployment:

```powershell
$env:Authentication__JwtKey = '<secret-with-at-least-32-characters>'
dotnet ef database update --project src/QueueFlow.Infrastructure --startup-project src/QueueFlow.Api
```

JWT key, senha do PostgreSQL e demais secrets devem ser fornecidos por variáveis de ambiente ou secret store. Não use valores reais em arquivos versionados.

## Ambiente completo com Docker

```powershell
docker compose up
```

O ambiente local possui credenciais de desenvolvimento por padrão. Para sobrescrevê-las, copie `.env.example` para `.env`; em produção, use obrigatoriamente um gerenciador de segredos.

O Compose aguarda PostgreSQL e Redis, aplica as migrations por um container de execução única e então inicia API, Worker e frontends. Endereços locais:

- Admin: http://localhost:3000
- Cliente: http://localhost:3001
- Display: http://localhost:3002
- API/Swagger: http://localhost:8080/swagger

`/health/live` verifica o processo da API; `/health` verifica PostgreSQL e Redis.

## Observabilidade

API e Worker emitem logs JSON estruturados com `CorrelationId`, traces e métricas OpenTelemetry via OTLP. O ambiente Docker inclui OpenTelemetry Collector, Prometheus e Alertmanager:

- Prometheus: http://localhost:9090
- Alertmanager: http://localhost:9093
- OTLP gRPC/HTTP: `localhost:4317` / `localhost:4318`

As regras em `observability/alerts.yml` cobrem indisponibilidade da telemetria, taxa elevada de erros HTTP, backlog anormal e falhas de notificação. O receiver local do Alertmanager deve ser substituído por e-mail, Slack, PagerDuty ou outro canal no ambiente de produção.

## Staging

O fluxo de promoção é `Development -> Staging -> Production`. Staging usa configuração, projeto Compose, volumes, portas e segredos próprios, mas as mesmas imagens e serviços destinados à produção.

```powershell
Copy-Item .env.staging.example .env.staging
# Substitua os dois segredos em .env.staging
docker compose --project-name queueflow-staging --env-file .env.staging --file docker-compose.yml --file docker-compose.staging.yml up --build --detach
./scripts/test-staging.ps1
```

O smoke test bloqueia a promoção se migrations, API, PostgreSQL/Redis, Admin, Customer, Display, negociação SignalR/WebSockets ou telemetria falharem. Em staging compartilhado, configure os quatro URLs públicos com HTTPS e `STAGING_SECURE_COOKIES=true` no `.env.staging`.

O ensaio da Etapa 29 executa bursts de 100 e 500 emissões, chamadas simultâneas por dez atendentes, reconexões SignalR e tentativas de leitura entre tenants. Ele falha se houver ID, senha/número ou token público duplicado:

```powershell
docker compose --project-name queueflow-staging --env-file .env.staging --file docker-compose.yml --file docker-compose.staging.yml --profile load run --build --rm load-test
```

Para encerrar o ambiente isolado:

```powershell
docker compose --project-name queueflow-staging --env-file .env.staging --file docker-compose.yml --file docker-compose.staging.yml down
```

## Frontends

Os workspaces `admin-web`, `customer-web` e `display-web` são compilados com:

```powershell
npm.cmd install
npm.cmd run build
```
