# E-mails de ativação via Resend

Configurar no ambiente da API:

- `Resend__ApiKey`: chave secreta com permissão de envio.
- `Resend__From`: remetente autorizado no Resend, por exemplo `QueueFlow <convites@seu-dominio.com>`.

Em Production/Staging, ambos habilitam códigos de verificação e convites de ativação.
Sem configuração válida, o provider permanece indisponível. `IsConfigured` verifica
a configuração local; não garante a validade da chave nem a autorização do remetente no provedor.
As variáveis devem ser configuradas no serviço da API; o `.env.example` não adiciona
automaticamente essas variáveis ao Docker Compose.

Development/Test preservam o comportamento anterior: códigos simulados sem acesso à
rede, e envio de convites indisponível, mesmo quando as variáveis estão preenchidas.

O adapter usa HTTPS, timeout de 15 segundos, sem redirects e sem retries automáticos.
Falhas HTTP/rede não são tratadas como sucesso nem expõem o corpo de erro do Resend.
Os logs do cliente HTTP estão desabilitados. Não há envio real nos testes.

O fluxo existente de `OrganizationInvitationService` permanece igual: o código e o
cooldown são persistidos antes do envio. Falha de entrega não desfaz esse estado;
a solicitação seguinte continua sujeita ao cooldown. A API mantém seu tratamento
atual de exceções. O fluxo de ativação não usa outbox nem adiciona recuperação
de senha. A configuração existente de URL pública e pepper de
verificação continua necessária.

Contrato utilizado: [Resend — Send Email](https://resend.com/docs/api-reference/emails/send-email).

## Comprovante de agendamento público

Configurar também `AppointmentEmails__CustomerPublicUrl` com a origem HTTPS do
Customer, sem caminho, query ou fragmento (por exemplo,
`https://queueflow-customer.onrender.com`). O endereço deve ser o domínio real do
Customer. API e Worker, quando utilizado, precisam dessa configuração e das
credenciais Resend. O `.env.example` apenas documenta os nomes.

O e-mail é obrigatório na criação pública. O comprovante é enfileirado na mesma
transação de um agendamento público `Confirmed`. Um agendamento `Scheduled` só
gera o comprovante após a confirmação existente no Admin. Nenhum status é
alterado pelo envio e não existe confirmação por e-mail. Um reagendamento
confirmado recebe seu próprio comprovante e PublicToken.

A API processa `appointment.receipt` separadamente do realtime, sem depender do
Worker. Ambos podem consumir a outbox com `FOR UPDATE SKIP LOCKED`. O comprovante
inclui cliente, empresa, unidade, serviço, horário no fuso salvo, link pessoal
`/meu-agendamento/{PublicToken}` e a orientação de check-in na unidade.

O transporte Resend é compartilhado com a ativação. Antes do envio, o conteúdo
completo (inclusive remetente e link) é persistido na outbox. Retentativas usam
o mesmo conteúdo e `Idempotency-Key`, inclusive após restart. Falha de configuração,
HTTP ou timeout mantém o agendamento e permite retry com o backoff existente.
Development/Test só enviam comprovantes se Resend e URL estiverem configurados;
os testes interceptam HTTP, sem entregar e-mails reais.

O Resend retém chaves por 24h. Para não duplicar uma entrega ambígua fora desse
prazo, 23h após a preparação a mensagem é suspensa: `ProcessedAt` permanece nulo,
`LastError` indica necessidade de revisão e `NextAttemptAt` recebe o valor máximo.
Verifique a entrega no provedor antes de qualquer reenvio manual. Não apague a
mensagem nem troque sua chave para forçar retry. Payloads contêm dados pessoais e
tokens; não devem ser copiados para logs. Nenhuma migration é necessária.

Referência: [Resend — Idempotency keys](https://resend.com/changelog/idempotency-keys).
