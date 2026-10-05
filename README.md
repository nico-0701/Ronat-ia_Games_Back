# Ronat-ia Games · Back

API e motor de jogos da plataforma **Ronat-ia Games** (nome provisório): jogos multiplayer para amigos, começando pela **Mímica**. A mesma conta e o mesmo backend atendem a **Web** e o **Android**.

> **Estado:** o backend está completo para o primeiro jogo: contas por telefone, grupos com senha compartilhada e membros sem conta, motor de partidas, a **Mímica**, tempo real (SignalR), ranking e histórico, e a documentação de publicação. Falta o Front, o importador dos dados da família e os próximos jogos. Veja [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
> Front (Web + Android): [`Ronat-ia_Games_Front`](https://github.com/nico-0701/Ronat-ia_Games_Front).

## O que a plataforma faz

- **Conta por telefone:** o número de telefone é o identificador único (sem SMS, sem senha). Cada pessoa tem nome e avatar (um pronto, à escolha, ou uma foto própria, que o servidor recorta, reduz e limpa de metadados).
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
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Visão geral da arquitetura: componentes, projetos, fluxos, dados, testes e limitações |
| [`docs/DATABASE.md`](docs/DATABASE.md) | Modelo de dados, conexão, ambiente local e migrações |
| [`docs/API.md`](docs/API.md) | Convenções, fluxo de entrada, grupos, partidas, endpoints e códigos de erro |
| [`docs/SECURITY.md`](docs/SECURITY.md) | Modelo de ameaças, riscos aceitos e controles |
| [`docs/GAME_DEVELOPMENT.md`](docs/GAME_DEVELOPMENT.md) | Como criar um novo jogo (o contrato dos módulos, regras de ouro e um exemplo) |
| [`docs/games/MIMICA.md`](docs/games/MIMICA.md) | O jogo Mímica: regras, configuração, ações, fases, a visão por jogador e o conteúdo |
| [`docs/REALTIME.md`](docs/REALTIME.md) | Tempo real (SignalR): conexão, métodos, mensagens, receita para o cliente e segurança |
| [`docs/DEPLOY.md`](docs/DEPLOY.md) | Publicação no Render e no Supabase, variáveis, operação e limites dos planos gratuitos |

## Como rodar

**Pré-requisitos:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (versão fixada em `global.json`) e um PostgreSQL local
para o banco de desenvolvimento e os testes de integração: **Docker** (`docker compose up -d db`) **ou** um PostgreSQL 16+ instalado
(`./scripts/dev-db.ps1` sobe um cluster privado, sem Docker). Detalhes em [`docs/DATABASE.md`](docs/DATABASE.md).

```bash
# 1. banco local (porta 54329)
docker compose up -d db          # ou, no Windows sem Docker:  ./scripts/dev-db.ps1

# 2. restaurar ferramentas, aplicar migrações, compilar e testar
dotnet tool restore
dotnet ef database update --project src/RonatIa.Games.Infrastructure --startup-project src/RonatIa.Games.Api
dotnet build
dotnet test

# 3. subir a API (http://localhost:5080)
dotnet run --project src/RonatIa.Games.Api
```

Com a API no ar, em desenvolvimento:

| URL | O que é |
|---|---|
| `http://localhost:5080/scalar/v1` | Documentação interativa da API (Scalar) |
| `http://localhost:5080/openapi/v1.json` | Contrato OpenAPI (também versionado em `docs/openapi/v1.json`) |
| `http://localhost:5080/health/live` | Processo respondendo |
| `http://localhost:5080/health/ready` | Processo e banco respondendo |
| `http://localhost:5080/api/v1/meta` | Versão da API, versão mínima do cliente, hora do servidor e estado de cadastro/captcha |

Teste rápido da entrada (o telefone é o único dado do login; veja [`docs/API.md`](docs/API.md)):

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/register -H 'Content-Type: application/json' \
  -d '{"phone":"(11) 98888-7777","displayName":"Nicole","avatarPreset":"preset-2","acceptTerms":true}'
```

**Segredos de desenvolvimento:** em `Development` a API usa valores **públicos e fracos** (`appsettings.Development.json`) para
`Auth:PhonePepper` e `Jwt:SigningKey`, só para rodar sem configurar nada; fora de Development ela se recusa a subir com eles.
Para usar valores próprios (ou um banco remoto), prefira `dotnet user-secrets set "Jwt:SigningKey" "<base64>" --project src/RonatIa.Games.Api`.

**Contrato OpenAPI:** o arquivo `docs/openapi/v1.json` é verificado por um teste. Se a API mudar, atualize-o com
`UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests` (PowerShell: `$env:UPDATE_OPENAPI=1`) e inclua-o no commit.

### Estrutura

```
src/
  RonatIa.Games.Api/              controllers finos, SignalR, autenticação, pipeline HTTP
  RonatIa.Games.Application/      casos de uso
  RonatIa.Games.Domain/           entidades e regras da plataforma
  RonatIa.Games.Abstractions/     contrato dos jogos (IGameModule): os módulos dependem só disto
  Games/RonatIa.Games.Mimica/      o jogo Mímica (regras puras e as 640 cartas)
  RonatIa.Games.Infrastructure/   EF Core/Npgsql, segurança, imagens
tests/
  RonatIa.Games.Domain.Tests/     testes unitários das regras de domínio
  RonatIa.Games.Abstractions.Tests/  contrato dos jogos e o exemplo de GAME_DEVELOPMENT.md
  RonatIa.Games.Mimica.Tests/     regras, segredo e baralho da Mímica (sem infraestrutura)
  RonatIa.Games.Infrastructure.Tests/  telefone, JWT, refresh token, Turnstile
  RonatIa.Games.Api.Tests/        testes de integração (API em memória + PostgreSQL real)
scripts/                          banco local (dev-db.ps1)
docs/                             ADRs, contrato OpenAPI e documentação
```

Segredos de desenvolvimento ficam fora do Git: use `dotnet user-secrets --project src/RonatIa.Games.Api` ou variáveis de ambiente (modelo em `.env.example`).

## Como contribuir

Leia o [CONTRIBUTING.md](CONTRIBUTING.md). Resumo: crie uma issue, uma branch (`feature/…`, `fix/…`), faça commits no padrão *Conventional Commits* e abra um pull request.

> ⚠️ **Este repositório é público.** Nunca versione segredos (`.env`, chaves, senhas), nem dados pessoais (fotos, nomes, telefones) de ninguém.

## Licença

A definir.
