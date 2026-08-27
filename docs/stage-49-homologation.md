# Etapa 49 — Homologação de agendamentos

## Estado

Implementação e validações estáticas concluídas. Homologação operacional pendente de atualizar o ambiente Docker com as migrations de agendamento.

O banco local em execução ainda não possui `Services.AttendanceMode`. Por segurança, as migrations não foram aplicadas automaticamente sobre os dados existentes.

## Cenários obrigatórios

- [ ] Organização A — serviço `QueueOnly`: emissão, chamada e conclusão sem regressão.
- [ ] Organização A — serviço `AppointmentOnly`: disponibilidade, reserva, confirmação, check-in e ticket único.
- [ ] Organização A — serviço `Hybrid`: agendado antecipado não chamado antes da hora; elegível priorizado; atrasado volta à ordenação normal; walk-in sem starvation.
- [ ] Organização B — repetir consultas públicas e administrativas, comprovando ausência de dados da organização A.
- [ ] Dois clientes disputando a última vaga: exatamente um `201`, outro `409`.
- [ ] Dois atendentes chamando simultaneamente: tickets distintos.
- [ ] Cancelamento e reagendamento dentro e fora do prazo.
- [ ] Lembretes 24h/2h/30min sem duplicação.
- [ ] No-show depois de `LateToleranceMinutes`.
- [ ] Atualização de Admin, Cliente, Atendente e Display via SignalR com REST como fonte da verdade.

## Evidências já obtidas

- 38 testes de domínio aprovados.
- 25 testes de aplicação aprovados.
- 2 testes de arquitetura aprovados.
- 14 testes de integração independentes de migrations aprovados.
- 4 testes PostgreSQL/Golden Path preparados e ignorados enquanto houver migrations pendentes.
- Builds aprovados para Admin Web, Attendant Web, Customer Web e Display Web.
- Script idempotente das migrations gerado com sucesso.
- Todos os containers atuais estão em execução; API, PostgreSQL e Redis reportam `healthy`.

## Migrations pendentes da funcionalidade

- `AddAppointmentScheduling`
- `AddServicePublicId`
- `AddAppointmentReminderStage`
- `AddAppointmentPrivacy`
