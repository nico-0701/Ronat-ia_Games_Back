# ADR-0002 — .NET 10 LTS, monólito modular e módulos de jogo puros

- **Status:** Aceito
- **Data:** 2026-10-04

## Contexto

A plataforma precisa de autenticação, grupos, partidas multiplayer em tempo real e vários jogos com regras diferentes, com orçamento zero de infraestrutura (planos gratuitos) e poucos desenvolvedores. O servidor deve ser a fonte da verdade das regras e do placar.

## Decisão

- **ASP.NET Core 10 (LTS)** em um **monólito modular**, uma instância, sem filas, cache distribuído nem microsserviços.
- Camadas: `Api` (controllers finos, SignalR, autenticação) → `Application` (casos de uso) → `Domain` (entidades e regras da plataforma); `Infrastructure` (EF Core/Npgsql, segurança, imagens). Dependências apontam para dentro.
- **Cada jogo é um módulo** (`RonatIa.Games.<Jogo>`) que depende **somente** de `RonatIa.Games.Abstractions`. Um módulo é uma máquina de estados **pura**: `(estado, ação, ator, contexto) → (novo estado, eventos, pontos)`, sem banco, rede ou relógio próprio. O módulo também decide **o que cada jogador pode ver** (projeção), para que informação secreta nunca vaze.
- Sem MediatR, AutoMapper ou repositórios genéricos: o `DbContext` já é a unidade de trabalho, e o mapeamento de DTOs é manual. (MediatR/AutoMapper passaram a ter licença comercial; FluentAssertions 8+ também.)
- Tempo "preguiçoso": prazos (`deadlineAt`) são dados no estado, avaliados quando chega uma ação ou leitura. Nada depende de *timers* em memória, pois a hospedagem gratuita dorme e reinicia.

## Consequências

- (+) Um deploy só; regras de jogo testáveis sem infraestrutura; adicionar um jogo não exige mexer em contas, grupos ou partidas.
- (+) Dependências de módulo verificadas pelo compilador (um módulo não enxerga EF, HTTP nem SignalR).
- (−) Uma instância só: sem *scale-out* no MVP. Se for preciso, o caminho é um *backplane* (Redis) para o SignalR e coordenação de estado por sessão.
- (−) Mais projetos na solução do que um monolito simples; aceito pelo ganho de isolamento.
