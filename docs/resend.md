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
atual de exceções. Esta integração não adiciona outbox, e-mails de filas/agendamentos
ou recuperação de senha. A configuração existente de URL pública e pepper de
verificação continua necessária.

Contrato utilizado: [Resend — Send Email](https://resend.com/docs/api-reference/emails/send-email).
