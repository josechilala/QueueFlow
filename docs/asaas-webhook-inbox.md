# Inbox durável de webhooks Asaas

## Fluxo

`POST /api/v1/billing/webhooks/asaas` mantém o gate Sandbox/checkout/tenant e a
autenticação em tempo constante pelo header `asaas-access-token`. Valida tamanho
e envelope, sem confiar em status, valores ou tenant do corpo.

O caso de uso `ReceiveBillingWebhook` depende de `IBillingWebhookInboxStore`.
A implementação PostgreSQL insere somente ID do evento, tipo, ID do pagamento e
metadados de processamento. Não persiste token, payload bruto nem dados pessoais.
Um índice único e `ON CONFLICT DO NOTHING` deduplicam entregas concorrentes.
Mesmo ID com outro tipo/pagamento retorna 409 sem sobrescrever o registro.
Recibos já existentes são lidos por MVCC, sem aguardar o lock do consumidor;
uma entrega duplicada não fica presa à consulta lenta de um pagamento no worker.

O endpoint responde **200 somente depois do commit**. A gravação tem orçamento
de cinco segundos. Falha de armazenamento retorna 503; o Asaas deve reenviar.
Nenhuma consulta HTTP ao Asaas ou liquidação ocorre na recepção. Eventos não
suportados também são recebidos duravelmente e concluídos como `Ignored`.

## Consumidor

`BillingWebhookWorker` é um `BackgroundService` registrado pela Infrastructure
quando `Asaas:Environment=Sandbox` e `Asaas:CheckoutEnabled=true`, tanto na API
quanto no projeto Worker. Não há configuração nova nem alteração automática
de variáveis externas. Instâncias simultâneas são permitidas.

O dispatcher abre uma transação e seleciona um registro vencido com
`FOR UPDATE SKIP LOCKED`. O processador participa da mesma transação, conserva
o lock por tenant e reconsulta o pagamento no Asaas antes de liquidar.
Mantém validação de ID, cliente, assinatura, valor, método e status. O tenant
vem do vínculo persistido, limitado a `Asaas:SandboxAllowedTenantId`.

Liquidação, ativação da assinatura, recibo em `BillingWebhookEvents` e conclusão
da inbox são confirmados atomicamente. Em falha, um savepoint desfaz todas as
alterações da liquidação; o tracker EF é limpo antes de persistir a retentativa.
Encerrar a conexão/processo antes do commit causa rollback e libera os locks:
outra instância recupera a entrada. Se o commit ocorreu mas o HTTP se perdeu,
a entrega repetida encontra o mesmo recibo, sem executar outra liquidação.

O worker usa escopos novos e cancellation token de encerramento, nunca
`Task.Run`, filas em memória ou o token da requisição original.

## Estados e falhas

| Estado | Significado |
| --- | --- |
| `Pending` | Recebido e aguardando processamento/retentativa |
| `Processed` | Liquidação/evento concluídos na mesma transação |
| `Ignored` | Evento não suportado ou reserva cancelada sem liquidação |
| `Review` | Intervenção operacional/financeira; sem retentativa automática |

Falhas de rede, timeout, HTTP 408/429/5xx e falhas transitórias de processamento
recebem backoff persistido de 5s, 10s, 20s… até 1h. Após 12 tentativas, a entrada
fica em `Review` com `RetryLimitExceeded`. Eventos ainda sem correspondência
local também usam retentativa limitada, cobrindo a corrida com a persistência
dos IDs pelo checkout. Falhas de banco que impedem registrar o retry deixam a
entrada original pendente por rollback.

Incompatibilidades de dados/tenant e HTTP 4xx definitivos vão para `Review`.
Nenhum evento é descartado por essas falhas. Mensagens de erro do provedor não
são persistidas; `LastErrorCode` contém apenas códigos fixos.

Reserva cancelada nunca é usada como origem de renovação ou reativação. Se há
dinheiro recebido/confirmado ou estorno, a entrada exige revisão financeira,
inclusive se o método/valor divergir. `PAYMENT_CREATED` de reserva cancelada com
dados compatíveis e sem liquidação é ignorado duravelmente. Divergência como
PIX/BOLETO gera revisão e não bloqueia a fila de entrega do Asaas.

## Migração e segurança

`20261015064151_AddBillingWebhookInbox` cria apenas a nova tabela, constraints,
índice único e índice de dispatch. Não altera nem remove dados das tabelas
existentes. O `Down` remove somente a inbox; não executar rollback depois de
receber eventos sem antes planejar a preservação dos recibos.

A nova tabela habilita RLS sem políticas de acesso público: os papéis de cliente
do Supabase/PostgREST não devem acessar envelopes ainda sem tenant confiável.
A conexão **servidor** precisa ser proprietária da tabela ou ter privilégio
explícito compatível com RLS. O teste descartável usa o proprietário. Confirmar
isso na revisão operacional da migração; não conceder acesso ao navegador.

Aplicar a migração antes de publicar a versão que usa a inbox. Ela foi exercitada
somente em PostgreSQL local descartável. Não há aplicação automática em Supabase.

## Operação pendente

- É necessário pelo menos um consumidor disponível. O processo Worker precisa
  receber a configuração Sandbox já existente; caso contrário, não registra o
  consumidor. A API também pode consumir quando habilitada.
- Render Free pode suspender o receptor/consumidor: a inbox não elimina o cold
  start anterior à recepção nem garante execução enquanto o processo dorme.
  Disponibilidade contínua é requisito operacional para o prazo do Asaas.
- Monitorar quantidade/idade de `Pending` e `Review`. Não há endpoint público
  de administração/reprocessamento nem devolução financeira automática.
- Revisar eventos `Review` depois de resolver a causa. Não resetar estados
  indiscriminadamente e não reativar reservas canceladas para absorver pagamentos.
- A entrega real e a eventual reativação da fila Asaas permanecem ações futuras
  explícitas; esta implementação não as executa.

Consulta operacional **somente leitura**, pelo canal administrativo autorizado:

```sql
SELECT "ProviderEventId", "EventType", "PaymentId", "Status", "Attempts",
       "ReceivedAt", "NextAttemptAt", "CompletedAt", "LastErrorCode"
FROM "BillingWebhookInbox"
WHERE "Status" IN ('Pending', 'Review')
ORDER BY "ReceivedAt";
```
