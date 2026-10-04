# ADR-0004 — Sessões: JWT curto, refresh rotativo e revogação imediata

- **Status:** Aceito
- **Data:** 2026-10-04

## Contexto

Com login só por telefone (ADR-0003) não há segredo que o usuário digite a cada acesso; a sessão é a única barreira depois do primeiro login. Os clientes são Web e Android (APK, que não se atualiza sozinho), e o SignalR precisa autenticar um WebSocket.

## Decisão

- **Access token:** JWT HS256 de 30 min com `sub`, `sid` e (admin) `role`; sem dado pessoal. A validação fixa o algoritmo, o emissor e a audiência.
- **Refresh token:** 256 bits aleatórios, guardado só como SHA-256, **uso único** (rotação a cada renovação), validade deslizante de 90 dias, uma linha por aparelho em `auth_sessions`.
- **Reutilização:** reapresentar um refresh token já trocado revoga a sessão, exceto numa janela de 15 s, que cobre a nova tentativa de quem perdeu a resposta por rede instável. Concorrência otimista (`version`) impede duas renovações simultâneas.
- **Revogação imediata:** a cada requisição autenticada o servidor confere sessão ativa e conta ativa (cache em memória de 10 s, invalidado ao revogar). Logout, "sair de todos os aparelhos" e suspensão valem na hora.
- **Limite de aparelhos:** 10 por conta; ao passar disso, o menos usado é desconectado.
- Sem cookies: tokens em `Authorization: Bearer`; o WebSocket do SignalR usa `access_token` na query **só** em `/hubs`.
- Segredos (`Jwt:SigningKey`, `Auth:PhonePepper`) validados na subida; a API não inicia com valor ausente, curto ou público de desenvolvimento fora de Development.

## Consequências

- (+) Um token roubado dura no máximo 30 min (e a reutilização do refresh derruba a sessão); logout e suspensão são imediatos.
- (+) Sem estado de sessão no cliente além dos dois tokens; fácil de testar com relógio injetado.
- (−) Uma consulta ao banco por requisição autenticada (amortizada pelo cache de 10 s). Com mais de uma instância o cache faria a revogação levar até 10 s para valer em todas.
- (−) HS256 com chave compartilhada: só o backend assina e valida, o que é suficiente; se um dia outro serviço precisar validar tokens, migrar para chaves assimétricas (JWKS).
- A chave pode ser rotacionada com `Jwt:PreviousSigningKey` durante a transição. O `PhonePepper`, **não**: trocá-lo invalida todos os logins.
