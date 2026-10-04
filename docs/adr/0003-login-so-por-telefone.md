# ADR-0003 — Login só por telefone, sem SMS e sem senha

- **Status:** Aceito (decisão do dono do produto)
- **Data:** 2026-10-04

## Contexto

O site é público, mas o produto é **um app de jogos para amigos**. O número de telefone serve apenas como **registro único** de cada pessoa. Verificar o telefone por SMS/WhatsApp tem custo por login (na ordem de US$ 0,06 a 0,11 por mensagem no Brasil) e foge do objetivo de custo zero.

## Decisão

- O **login é o número de telefone** (normalizado para E.164, região padrão BR). Sem SMS, sem OTP, sem senha.
- Primeiro acesso: o telefone não existe → o cliente pede **nome** e **avatar** (um preset ou foto enviada) e cria a conta.
- Sessão: *access token* JWT curto + *refresh token* opaco e rotativo, guardado só como hash, uma sessão por aparelho, com listagem e revogação.
- O telefone é guardado **apenas como HMAC-SHA256** (segredo do servidor) mais os 4 últimos dígitos para exibição. Nunca é exposto a outros usuários.
- **Quem protege os dados de um grupo é a senha do grupo** (código de entrada gerado pelo sistema e compartilhado entre os amigos), não o login.
- A identidade de cada requisição vem **do token**, nunca de um telefone enviado pelo cliente.

## Consequências

- (+) Entrada sem atrito, custo zero, nenhuma dependência externa de autenticação.
- (−) **Risco aceito:** como o telefone não é verificado, *quem souber o número de alguém consegue entrar na conta dessa pessoa* e alguém pode cadastrar o número de outra pessoa antes dela. O dano é limitado ao nome, ao avatar e aos grupos dessa conta.
- Mitigações: limites de taxa por IP e por telefone; Cloudflare Turnstile opcional (ligado quando a chave estiver configurada); `Registration:Mode=closed` como válvula de escape; telefone nunca exibido; sessões listáveis e revogáveis; entrada em grupo exige a senha do grupo, com limite de tentativas.
- Se houver abuso, o caminho de evolução é **acrescentar verificação** (código por WhatsApp/SMS ou um PIN) atrás de uma configuração, sem trocar o modelo de contas.
