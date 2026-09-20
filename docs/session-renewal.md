# Renovação de sessão

Admin, Platform e Attendant mantêm access cookies de 15 minutos e refresh cookies
de 30 dias, HttpOnly, SameSite=Lax, restritos ao host e Secure em Production.
O logout explícito continua removendo apenas os cookies da aplicação/identidade.

Quando falta o access cookie, o proxy encaminha a navegação para o handler de
refresh, preservando o destino local. Server Components encaminham apenas 401
para renovação: não rotacionam tokens sem poder gravar cookies. O layout Platform
mostra acesso negado em 403 e uma tela com nova tentativa para falhas temporárias.

Os BFFs renovam após 401 nas operações autenticadas e repetem a operação uma vez.
429, 5xx, timeout, falhas de rede e respostas inválidas preservam os cookies.
Mesmo quando a operação posterior falha, o novo par é gravado para não perder a
rotação já confirmada pela API. `Retry-After` e `Cache-Control: no-store` são mantidos.
Somente 401 com o código explícito `auth.invalid_refresh` ou
`platform.invalid_refresh` remove os cookies automaticamente. Uma API antiga que
não retorne esse código resulta em indisponibilidade recuperável, sem logout.

O BFF compartilha renovações em andamento por hash de origem, endpoint e token.
O resultado bem-sucedido fica disponível por cinco segundos para requisições que
já carregavam o cookie anterior. O cache é limitado e não persiste tokens em disco.
Falhas não são armazenadas. Na API, transações com `FOR UPDATE` garantem uma única
rotação também entre processos. Tokens já rotacionados recebem 409, sem emissão
de credenciais: o BFF preserva os cookies para não apagar o resultado da requisição
vencedora em outra instância. Não foi introduzida uma janela de replay na API.

Se a resposta vencedora se perder antes de chegar ao navegador e o cache local
já não estiver disponível, não se recupera o novo token usando o token revogado.
O erro mantém os cookies; o usuário pode entrar novamente. Isso preserva a
revogação de uso único sem distribuir cópias recuperáveis dos refresh tokens.

Restart não remove refresh tokens do PostgreSQL nem cookies do navegador.
Banco, issuer, audience e chave JWT devem permanecer estáveis entre instâncias.
Não há migration nova. Login mantém sua política e limite anteriores; refresh
tem cota própria, descrita em `trusted-proxies.md`.
