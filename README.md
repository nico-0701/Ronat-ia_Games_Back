# Ronat-ia Games · Back

API e motor de jogos da plataforma **Ronat-ia Games** (nome provisório): jogos multiplayer para amigos, começando pela **Mímica**. A mesma conta e o mesmo backend atendem a **Web** e o **Android**.

> **Estado:** em construção. Acompanhe as [issues](../../issues) e os [pull requests](../../pulls).
> Front (Web + Android): [`Ronat-ia_Games_Front`](https://github.com/nico-0701/Ronat-ia_Games_Front).

## O que a plataforma faz

- **Conta por telefone:** o número de telefone é o identificador único (sem SMS, sem senha). Cada pessoa tem nome e avatar (presets ou foto própria).
- **Grupos de amigos:** quem cria o grupo recebe uma **senha do grupo**, que é compartilhada com quem for entrar. Grupos também podem ter membros **sem conta** (perfis com nome e foto, geridos pelo dono e "reivindicáveis" depois).
- **Partidas multiplayer:** lobby → times → rodadas → pontuação → resultado → ranking, em tempo real (SignalR). O servidor é a **fonte da verdade** das regras e do placar.
- **Jogos como módulos:** cada jogo é um módulo independente (Mímica primeiro; "adivinhar o ano" e "Out of the Loop" depois). Adicionar um jogo não exige mexer em contas, grupos ou partidas.

## Stack

| Camada | Tecnologia |
|---|---|
| API | ASP.NET Core 10 (LTS), C#, controllers finos |
| Banco | PostgreSQL 17 (Supabase), EF Core 10 + Npgsql, migrações |
| Tempo real | SignalR |
| Hospedagem | Render (Docker, região Oregon) |
| Testes | xUnit, `WebApplicationFactory`, PostgreSQL real |

Decisões de arquitetura: [`docs/adr`](docs/adr).

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/adr`](docs/adr) | Registros de decisões de arquitetura (ADRs) |
| `docs/ARCHITECTURE.md` | Visão geral da arquitetura *(em breve)* |
| `docs/DATABASE.md` | Modelo de dados *(em breve)* |
| `docs/API.md` | Convenções e contrato da API *(em breve)* |
| `docs/SECURITY.md` | Modelo de ameaças e riscos aceitos *(em breve)* |
| `docs/GAME_DEVELOPMENT.md` | Como criar um novo jogo *(em breve)* |
| `docs/DEPLOY.md` | Render, Supabase, variáveis e limites dos planos gratuitos *(em breve)* |

## Como rodar

*(será detalhado junto com o esqueleto da solução)*

## Como contribuir

Leia o [CONTRIBUTING.md](CONTRIBUTING.md). Resumo: crie uma issue, uma branch (`feature/…`, `fix/…`), faça commits no padrão *Conventional Commits* e abra um pull request.

> ⚠️ **Este repositório é público.** Nunca versione segredos (`.env`, chaves, senhas), nem dados pessoais (fotos, nomes, telefones) de ninguém.

## Licença

A definir.
