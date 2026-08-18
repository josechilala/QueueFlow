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

Com Docker disponível, copie `.env.example` para `.env`, escolha uma senha local e execute `docker compose up --build`. O endpoint `/health/live` verifica o processo; `/health` também verifica o PostgreSQL.

A migration inicial foi criada na Fase 1 junto às primeiras entidades e deve ser aplicada somente por uma etapa controlada de deployment.

## Banco e segurança

A migration inicial está versionada, mas nunca é executada automaticamente pela API. Aplique-a somente em uma etapa controlada de deployment:

```powershell
$env:Authentication__JwtKey = '<secret-with-at-least-32-characters>'
dotnet ef database update --project src/QueueFlow.Infrastructure --startup-project src/QueueFlow.Api
```

JWT key, senha do PostgreSQL e demais secrets devem ser fornecidos por variáveis de ambiente ou secret store. Não use valores reais em arquivos versionados.

## Frontends

Os workspaces `admin-web`, `customer-web` e `display-web` são compilados com:

```powershell
npm.cmd install
npm.cmd run build
```
