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

**Pré-requisitos:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (versão fixada em `global.json`).
Para o banco local e os testes de integração: um PostgreSQL 17+ (instruções de banco local virão junto com a Fase 4).

```bash
# restaurar, compilar e testar
dotnet build
dotnet test

# subir a API (http://localhost:5080)
dotnet run --project src/RonatIa.Games.Api
```

Com a API no ar, em desenvolvimento:

| URL | O que é |
|---|---|
| `http://localhost:5080/scalar/v1` | Documentação interativa da API (Scalar) |
| `http://localhost:5080/openapi/v1.json` | Contrato OpenAPI (também versionado em `docs/openapi/v1.json`) |
| `http://localhost:5080/health/live` | Processo respondendo |
| `http://localhost:5080/api/v1/meta` | Versão da API, versão mínima do cliente e hora do servidor |

**Contrato OpenAPI:** o arquivo `docs/openapi/v1.json` é verificado por um teste. Se a API mudar, atualize-o com
`UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests` (PowerShell: `$env:UPDATE_OPENAPI=1`) e inclua-o no commit.

### Estrutura

```
src/
  RonatIa.Games.Api/              controllers finos, SignalR, autenticação, pipeline HTTP
  RonatIa.Games.Application/      casos de uso
  RonatIa.Games.Domain/           entidades e regras da plataforma
  RonatIa.Games.Infrastructure/   EF Core/Npgsql, segurança, imagens
tests/
  RonatIa.Games.Api.Tests/        testes de integração (API em memória)
docs/                             ADRs, contrato OpenAPI e documentação
```

Segredos de desenvolvimento ficam fora do Git: use `dotnet user-secrets --project src/RonatIa.Games.Api` ou variáveis de ambiente (modelo em `.env.example`).

## Como contribuir

Leia o [CONTRIBUTING.md](CONTRIBUTING.md). Resumo: crie uma issue, uma branch (`feature/…`, `fix/…`), faça commits no padrão *Conventional Commits* e abra um pull request.

> ⚠️ **Este repositório é público.** Nunca versione segredos (`.env`, chaves, senhas), nem dados pessoais (fotos, nomes, telefones) de ninguém.

## Licença

A definir.
