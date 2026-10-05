# ADR-0009 — Ranking calculado dos resultados e limpeza automática

- **Status:** Aceito
- **Data:** 2026-10-05

## Contexto

Os grupos querem saber "quem ganha mais" e rever as partidas. Os dados já existem: cada partida encerrada grava um resultado por jogador (`session_results`). Ao mesmo tempo, o plano gratuito do Supabase tem 500 MB e o servidor dorme, e partidas esquecidas (um lobby aberto e abandonado) ocupariam para sempre uma das vagas de partidas abertas do grupo.

## Decisão

- **O ranking é calculado na hora** a partir de `session_results` (agregação por membro, apoiada pelo índice `(group_id, game_id, member_id)`), sem tabela de ranking nem cache: o volume de um grupo de amigos é pequeno, e o cálculo nunca fica defasado. O critério é vitórias, depois aproveitamento, depois partidas; empate em tudo divide a posição.
- **O histórico pertence ao membro do grupo** (`member_id`), não à conta: um perfil sem conta que alguém assume depois leva o que já jogou, e quem sai do grupo continua aparecendo com o que ganhou. Só contam partidas **encerradas**.
- **Empate de partida = todos vencem**, como no app original: a vitória é decidida pelo jogo (`PlayerStanding.IsWinner`), a plataforma só soma.
- **Limpeza automática** num serviço em segundo plano (de hora em hora, desligável): cancela lobbies sem mudança há 12 h e partidas sem ação há 24 h; apaga a trilha de eventos de partidas encerradas há mais de 60 dias (o placar, o livro-razão e o resultado ficam) e sessões de login expiradas ou encerradas há mais de 30 dias. É feita em comandos SQL em lote. Só roda com a API acordada, e **nada do jogo depende dela**: é higiene.

## Consequências

- (+) Ranking sempre correto e sem manutenção; o histórico acompanha as pessoas, não as contas.
- (+) O banco gratuito não cresce sem limite, e um lobby esquecido não bloqueia o grupo.
- (−) A trilha de eventos fica disponível por 60 dias: depois, só o resultado final permanece (não dá para reconstituir lance a lance).
- (−) Se um grupo juntar milhares de partidas, a agregação por consulta pode pesar; nesse caso, uma tabela de totais por membro e jogo, atualizada ao encerrar a partida, resolveria sem mudar a API.
- (−) Com várias instâncias o serviço de limpeza rodaria em todas; os comandos são idempotentes, então o efeito é só repetido.
