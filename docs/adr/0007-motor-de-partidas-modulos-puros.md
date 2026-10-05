# ADR-0007 — Motor de partidas: módulos de jogo puros, estado opaco, versão e idempotência

- **Status:** Aceito
- **Data:** 2026-10-05

## Contexto

A plataforma vai hospedar vários jogos (Mímica, "adivinhar o ano", Out of the Loop) com necessidades diferentes: times ou cada um por si, informação secreta por jogador (a carta do mímico, o papel de cada um), fases com prazo, ações simultâneas. O cliente não é confiável (o placar e as regras são do servidor), a hospedagem gratuita dorme e reinicia (não dá para depender de timers em memória) e várias pessoas agem ao mesmo tempo no celular. Acrescentar um jogo não pode exigir mexer em contas, grupos nem no ciclo de vida das partidas.

## Decisão

- **Cada jogo é um módulo puro** que implementa `IGameModule` (projeto `RonatIa.Games.Abstractions`, sem dependências; um módulo depende só dele): uma máquina de estados `(estado, ação, quem age, contexto) → (novo estado, eventos, pontos)` mais `Start`, `ValidateConfig`, `Project` e `Finish`. Sem banco, rede ou relógio próprios: o horário e o sorteio vêm do `IGameContext`, o que torna o jogo testável e determinístico. Um módulo recusa uma ação lançando `RuleViolation` (400 ação malformada, 403 sem permissão, 409 fase ou prazo errados), com um `code` estável `jogo.motivo`.
- **O estado é opaco para a plataforma:** um JSON com `schemaVersion`, guardado em `game_sessions.state` (jsonb) e interpretado só pelo módulo. A plataforma cuida do que é comum: ciclo de vida (lobby → em andamento → encerrada/cancelada), jogadores, times, configuração, eventos, pontuação, resultado, versão e idempotência.
- **Segredo só sai pela projeção:** o estado nunca é devolvido; cada pessoa recebe `Project(estado, quem consulta)`, com as `allowedActions` calculadas pelo servidor (o cliente não deduz o que é válido). Quem só assiste (membro do grupo que não joga) passa pela mesma projeção, sem `playerId`. Há testes de vazamento: a palavra secreta não aparece em nenhuma resposta para quem não pode vê-la (partida, trilha de eventos, listagem).
- **Tempo é dado:** os prazos ficam no estado (ex.: `deadlineAt`) e o módulo os avalia quando chega uma ação ou uma leitura, com o `Now` injetado. Não há timers. Consequência aceita: um turno parado continua parado até alguém agir; o anfitrião pode pular.
- **Os jogadores são membros do grupo** (com conta ou perfis sem conta, ADR-0006): o "convidado sem celular" do app antigo é um perfil do grupo, e o histórico e o ranking continuam atrelados ao membro, mesmo que a pessoa assuma o perfil depois.
- **Uma ação, uma gravação:** novo estado + evento da ação + eventos do módulo + livro-razão de pontos (+ resultado, se acabou) em **uma transação**. `version` é o token de concorrência: se duas ações chegam juntas, a segunda **refaz a leitura e reavalia sobre o estado novo** (pode virar uma recusa legítima do módulo), até 6 tentativas; persistindo, `409 session.concurrent_update`. O mesmo vale para começar e encerrar.
- **Idempotência por `clientActionId`** (índice único por partida): reenviar uma ação (rede ruim, duas abas) devolve o estado atual com `replayed: true`, sem reaplicar. A trilha tem um evento `action.<tipo>` por ação (que carrega o id, **sem o conteúdo**: um palpite pode ser segredo) seguido dos eventos públicos do módulo; a sequência é contínua e única por partida.
- **Pontos só pelo servidor:** o módulo devolve `ScoreChange` (jogador, time ou os dois); o total de cada jogador e time é a **soma do livro-razão**. No fim, o módulo classifica (`Finish`) e a plataforma grava `session_results`, base dos rankings. O anfitrião pode encerrar antes (vale o placar do momento); empate é decisão do módulo.
- **Quem gerencia** o lobby, começa, cancela e encerra: o anfitrião (quem criou) ou administradores do grupo (um grupo não fica travado se o anfitrião some). Para o módulo, `GameActor.IsHost` indica isso.
- **O catálogo vive no código** (módulos registrados na DI): não há tabela de jogos. A partida guarda `rules_version` do jogo na criação.
- **Limites:** 5 partidas abertas por grupo, 240 ações/min e 30 criações/h por pessoa; a trilha é paginada.

## Consequências

- (+) Um jogo novo é um projeto novo que depende só de `Abstractions`, com testes de regras sem infraestrutura; o motor é validado por jogos de teste (`relay`, com times, segredo e prazo; `solo`, sem times) independentes de qualquer jogo real.
- (+) Segurança por construção: segredo preso à projeção, permissões por ação no módulo, concorrência e idempotência garantidas pelo banco (versão, índices únicos) e testadas com requisições simultâneas.
- (−) O estado em JSON não se consulta bem por SQL: análises usam eventos e resultados, não o estado.
- (−) Como uma ação pode ser reavaliada numa nova tentativa, o módulo precisa ser determinístico além do `ctx` (guarde sementes no estado, não use relógio nem sorteio próprios).
- (−) Módulo com erro (ex.: classificar um jogador que não existe) vira 500; os testes do módulo precisam cobrir o `Finish`.
- (−) A retenção da trilha (`game_events` antigos) e o encerramento automático de partidas abandonadas ainda não existem; a correção não depende deles.
