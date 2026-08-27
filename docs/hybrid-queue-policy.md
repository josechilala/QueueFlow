# Política operacional do modo híbrido

O `Appointment` só participa da fila depois de um check-in válido gerar seu único `QueueTicket`. A escolha do próximo ticket acontece exclusivamente no backend e dentro da mesma transação PostgreSQL usada pelo `CallNext`.

## Ordenação

1. Tickets de agendamento cujo horário ainda não chegou são excluídos dos candidatos.
2. Um walk-in aguardando há pelo menos uma duração de slot recebe prioridade por envelhecimento. Esse limite acompanha a configuração do serviço e impede starvation sem usar proporção fixa.
3. Havendo capacidade reservada, agendamentos dentro da janela `ScheduledStart` até `ScheduledStart + LateToleranceMinutes` vêm antes dos walk-ins comuns. Entre eles: `ScheduledStart`, `CheckedInAt`, `SequenceNumber`.
4. A prioridade de agendamento fica limitada a `CapacityPerSlot` atendimentos agendados simultaneamente em `Called` ou `InService`.
5. Agendamentos atrasados perdem a prioridade temporal e entram na ordenação normal.
6. A ordenação normal preserva `Priority DESC`, `IssuedAt ASC`, `SequenceNumber ASC`.

O seletor usa `FOR UPDATE OF ticket SKIP LOCKED`, portanto dois atendentes não recebem o mesmo ticket. Tickets já chamados ou em atendimento nunca são reordenados ou interrompidos.
