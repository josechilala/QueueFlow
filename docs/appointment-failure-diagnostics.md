# Diagnóstico de 500 no agendamento público

A causa do incidente de produção ainda não está comprovada. Esta alteração acrescenta diagnóstico; não modifica regras de agendamento, timezone, persistência, autenticação ou rate limiting.

## Caminho confirmado no código

- `booking-form.tsx`: `Continuar` abre o resumo. A confirmação envia o POST.
- Criação: `/api/appointments` encaminha para `/api/v1/public/appointments`.
- Reagendamento: `/api/appointments/{publicToken}/reschedule` encaminha para `/api/v1/public/appointments/{publicToken}/reschedule`.
- A URL da página usa `/agendamento/{slug}/{branchPublicId}/{servicePublicId}`. O parâmetro `reschedule` é propagado para o formulário, inclusive ao consultar outra data.
- Criação envia `branchPublicId`, `servicePublicId`, `scheduledStart`, nome, e-mail e telefone opcional (`null`). Não envia um OrganizationId confiado ao navegador: a API resolve a organização pela unidade e verifica o serviço dentro dela.
- Reagendamento envia somente `scheduledStart`; os dados do cliente e da organização vêm do agendamento identificado pelo token.
- `scheduledStart` é preservado como veio do slot. A infraestrutura converte para o timezone da unidade para verificar a disponibilidade e usa o instante UTC do slot na entidade.
- Os proxies não repetem o POST e preservam status/body da API. O diagnóstico também propaga `Retry-After` e fornece `X-Correlation-ID`.

## Persistência e e-mail

`PostgresAppointmentOperations.CreateAsync` e `RescheduleAsync` adquirem locks, verificam o slot, preparam os eventos e o recibo na outbox, executam `SaveChangesAsync` e fazem commit na mesma transação.

O envio de e-mail ocorre posteriormente pelo processamento da outbox. Falha no provedor de e-mail não transforma uma criação já concluída em 500 nesse caminho.

No reagendamento, `PublicAppointmentService.RescheduleAsync` consulta o resumo depois que a operação de persistência retorna. Uma falha nessa leitura pode ocorrer depois do commit. Não foi comprovado que seja o incidente relatado.

## Logs a procurar no Render

Após a disponibilização deste diagnóstico, na próxima tentativa legítima que falhar:

1. Anote o endpoint, status e `X-Correlation-ID` da resposta no DevTools. O ProblemDetails da API também contém `correlationId`.
2. No serviço Customer Web, procure `appointment_proxy_failure` com esse `correlationId`.
   - `stage=api_response`: a API devolveu o status registrado.
   - `stage=api_request`: falha de transporte durante a chamada; não é evidência de que a API rejeitou ou deixou de persistir.
   - `stage=read_response`: falha ao ler a resposta.
3. No serviço da API, procure `Unhandled request error` com o mesmo `CorrelationId`. `Endpoint` contém o template, nunca o token real.
4. No mesmo grupo, procure `Appointment operation failed`. Os campos são `Operation`, `Stage`, `OrganizationId`, `ServiceId`, `AppointmentId`, `ExceptionType`, `CauseType` e `SqlState`. `CorrelationId` vem do contexto estruturado existente.
5. Verifique se existe `Appointment transaction committed` com o mesmo identificador de correlação. Esse registro confirma que o commit retornou com sucesso. Um 500 posterior exige investigar a construção/leitura da resposta.

Etapas instrumentadas: validação de identificadores, abertura da transação, consulta da unidade/organização/serviço, lock do agendamento/configuração, interpretação do horário, consulta de horários/bloqueios/reservas, geração do slot, construção da entidade, preparação dos eventos/recibo, gravação e commit.

A ausência do registro de commit não prova ausência de persistência: uma falha de conexão durante o commit pode deixar o resultado indeterminado. Não repetir automaticamente o POST com base apenas nisso.

Enviar somente esses registros estruturados. Não enviar o body da requisição, URL com token, senha, nome, telefone, e-mail ou credenciais. Os novos logs não incluem mensagens de exceção, SQL, bodies ou tokens.

## Limites desta investigação

Criação e reagendamento válidos foram reproduzidos localmente com PostgreSQL e timezone `America/Sao_Paulo`. Foram injetadas falhas de INSERT para verificar rollback de agendamento/outbox e os diagnósticos dos dois caminhos. Isso não identifica a exceção de produção nem comprova que seu schema está desatualizado.

Sem o registro correspondente, não foi aplicada uma correção especulativa de regras, schema ou UX. O tratamento existente do formulário ainda não garante liberação de loading em rejeições de transporte; essa alteração não afirma resolver esse ponto nem o 500 de produção.

Nenhuma variável do Render ou migration nova é necessária para esta instrumentação. Não há indicação comprovada para alterar dados/schema de produção. A ação pendente é correlacionar a falha real com os logs acima.
