# QueueFlow — site institucional

Frontend independente com Next.js, React e TypeScript. Exportação estática; o formulário de teste envia solicitações à API pública do QueueFlow, sem autenticação ou SignalR.

Execute na raiz do repositório:

```sh
npm ci
npm run dev --workspace=@queueflow/marketing-web
```

Acesse http://localhost:3004. No Windows, use `npm.cmd` se o PowerShell bloquear `npm.ps1`.

```sh
npm run test --workspace=@queueflow/marketing-web
npm run build --workspace=@queueflow/marketing-web
```

O build gera `apps/marketing-web/out`, incluindo `/`, `/termos/` e `/privacidade/`. Para conferir a exportação com Python instalado, execute `python -m http.server 3004 --directory apps/marketing-web/out` na raiz e acesse http://localhost:3004.

## Solicitação de teste grátis

Copie `.env.example` para `.env.local` dentro deste aplicativo e configure `NEXT_PUBLIC_QUEUEFLOW_API_URL` com a origem HTTPS da API (sem caminho). Em `next dev`, também é permitido `http://localhost:5260`. A variável é incorporada durante o build; alterações exigem nova exportação. Sem origem válida, o formulário fica indisponível com mensagem explícita.

Na API, inclua a origem do site em `Cors:Origins` (por exemplo, `Cors__Origins__1`), aplique a migration `20260922043400_AddTrialRequests` e mantenha configurado o envio de e-mail de convites existente. `RateLimiting:TrialRequestPermitLimit` controla o limite público por IP a cada dez minutos (padrão: 5).

Todos os botões “Começar teste grátis” continuam levando ao CTA final. O formulário pede nome, e-mail, empresa, telefone e aceite obrigatório de `/termos` e `/privacidade`. O envio registra uma solicitação pendente, sem criar conta nem iniciar o Trial. Solicitações repetidas para o mesmo e-mail recebem a mesma confirmação sem expor ou alterar dados anteriores.

Em **Plataforma → Solicitações de teste**, o administrador consulta detalhes e aprova ou recusa. Aprovar cria e envia um convite pelo serviço existente e vincula a decisão à solicitação, com auditoria. Se o envio falhar, a solicitação permanece pendente. Apenas a verificação e ativação existentes criam Organization/Owner e iniciam o Trial de 14 dias. Não há pagamento nesta etapa.

## Conteúdo e publicação

- Entrar sempre aponta para https://app.queueflow.com.br.
- Benefícios, recursos, etapas, segmentos e FAQ ficam em `content/site-content.ts`.
- Prévias do produto têm dados explicitamente ilustrativos.
- Termos e Privacidade são minutas sinalizadas, com `noindex`, e precisam de aprovação jurídica antes da publicação comercial.
- O ano do rodapé é calculado durante o build.
- Favicon vetorial criado com a inicial e as cores da identidade QueueFlow; não há imagens ou fontes externas.
- Este aplicativo não altera CI, serviços Render, DNS ou os outros frontends.
- A raiz já compila todos os workspaces; este aplicativo passa a participar desse comando por estar em `apps/*`.

## Interatividade

- A demonstração tem cinco slides em React/CSS, com rotação a cada 5 segundos, setas, indicadores e controle de pausa. Setas do teclado, Home e End também navegam entre os slides.
- A interação manual suspende a rotação por 15 segundos; foco, hover, aba oculta e saída da área visível também pausam a troca. O intervalo normal recomeça quando todas as pausas terminam.
- A preferência por movimento reduzido desliga a rotação automática, as transições e as entradas de scroll. Os controles manuais continuam disponíveis.
- As entradas no scroll ocorrem uma vez. O conteúdo permanece disponível sem JavaScript.
- Os testes de interação usam o ambiente jsdom já presente no monorepo. Execute a instalação a partir da raiz.
- Para a checagem opcional em navegador, sirva a exportação na porta 3004 e execute, na raiz, `node apps/marketing-web/tests/browser-smoke.mjs`. O script usa Chrome headless instalado (caminho Windows padrão, ou variável `CHROME_PATH`), sem biblioteca adicional. Capturas ficam em `.next/visual-check` deste aplicativo.
