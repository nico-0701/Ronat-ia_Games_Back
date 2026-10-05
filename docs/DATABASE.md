# Banco de dados

PostgreSQL 17 (Supabase), acessado **somente pelo backend** (EF Core + Npgsql). Nenhum cliente (Web, Android) fala com o banco.

## Princípios

| Tema | Decisão |
|---|---|
| **Schema** | Todas as tabelas ficam no schema `app`. O schema `public` fica **vazio**: o Supabase o expõe pela Data API (PostgREST) com a chave `anon`, que é pública. |
| **RLS** | Row Level Security **habilitado em toda tabela, sem políticas**: quem não é dono (as funções `anon` e `authenticated`) não enxerga nada. É uma segunda barreira; o backend conecta como dono e não é afetado. Um teste falha se alguma tabela ficar sem RLS. |
| **IDs** | `uuid` v7 gerado pela aplicação (`Guid.CreateVersion7`), que mantém a ordem de inserção e dispensa `uuidv7()` do PostgreSQL 18. |
| **Tempo** | `timestamptz`, sempre em UTC. |
| **Enums** | Texto em `snake_case` + `CHECK` (migrações mais simples que enums nativos). |
| **Nomes** | `snake_case` para tabelas, colunas, índices (`ix_`, `ux_`), chaves (`pk_`, `fk_`) e checks (`ck_`). |
| **Telefone** | Nunca em claro: só `phone_hash` (HMAC-SHA256 com segredo do servidor, 32 bytes, **único**) e `phone_last4` para exibição. |
| **Estado de jogo** | Colunas `jsonb` com `schema_version`; a plataforma não interpreta o conteúdo, só o módulo do jogo. |

## Tabelas atuais

### `app.users`

Conta de uma pessoa.

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK |
| `phone_hash` | bytea | **UNIQUE**; `CHECK (octet_length = 32)` |
| `phone_last4` | char(4) | `CHECK ~ '^[0-9]{4}$'` |
| `display_name` | varchar(60) | `CHECK (1 a 60 caracteres)`; a regra de negócio (2 a 30) fica no domínio |
| `avatar_preset` / `avatar_photo_id` | varchar(20) / uuid | **exatamente um** dos dois (`ck_users_avatar_one_of`); `avatar_photo_id` → `avatars.id` |
| `status` | varchar(20) | `active`, `suspended`, `deleted` |
| `is_admin` | boolean | |
| `created_at`, `updated_at`, `last_login_at`, `deleted_at` | timestamptz | |
| `terms_accepted_at`, `terms_version` | timestamptz, varchar(20) | registro de aceite (LGPD) |

Exclusão de conta: a linha é **anonimizada**, não apagada (nome "Jogador removido", `phone_hash` aleatório, que libera o número), para o histórico de partidas continuar íntegro.

### `app.avatars`

Foto de avatar já processada (recortada, reduzida e reencodada em WebP, sem metadados). Fica no próprio banco: são poucos KB por foto, e isso mantém tudo transacional.

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK (também é o endereço, não adivinhável, da imagem) |
| `uploaded_by_user_id` | uuid | FK `users.id`, `ON DELETE SET NULL` |
| `content_type`, `data`, `width`, `height`, `sha256` | | `CHECK` de 1 byte a 512 KB e `sha256` com 32 bytes |

### `app.auth_sessions`

Uma linha por aparelho conectado. Guarda só o **hash** do *refresh token*; a cada uso o token é trocado e o anterior fica em `previous_token_hash`, para detectar reutilização (sinal de token roubado).

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK |
| `user_id` | uuid | FK `users.id`, `ON DELETE CASCADE` |
| `token_hash` | bytea | **UNIQUE**, 32 bytes |
| `previous_token_hash`, `rotated_at` | bytea, timestamptz | índice parcial (`previous_token_hash IS NOT NULL`) |
| `device_label`, `created_at`, `last_used_at`, `expires_at`, `revoked_at`, `revoked_reason` | | índices em `user_id` e `expires_at` |

### `app.groups`

Grupo de amigos (ADR-0006). Excluir é **lógico** (`deleted_at`).

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK |
| `name` | varchar(80) | `CHECK (1 a 80 caracteres)`; a regra de negócio (2 a 40) fica no domínio |
| `invite_code` | char(8) | a *senha do grupo*; **UNIQUE** (inclusive entre grupos excluídos: o código nunca é reaproveitado); `CHECK ~ '^[A-HJ-KM-NP-TV-Z2-9]{8}$'` (sem I, L, O, U, 0 e 1) |
| `invite_enabled` | boolean | desligada, ninguém novo entra |
| `created_at`, `updated_at`, `deleted_at` | timestamptz | |

### `app.group_members`

Quem participa de um grupo: **pessoa com conta** (`user_id` preenchido; nome e avatar são os da conta) ou **perfil sem conta** (`user_id` nulo, com nome e avatar próprios, que alguém pode assumir ao entrar). **A linha nunca é apagada**: sair ou ser removido só muda o `status`, para o histórico de partidas continuar apontando para o membro.

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK (identifica a pessoa **no grupo**, não na plataforma) |
| `group_id` | uuid | FK `groups.id`, `ON DELETE CASCADE` |
| `user_id` | uuid, nulo | FK `users.id`, `ON DELETE RESTRICT` (contas são anonimizadas, nunca apagadas); nulo = perfil sem conta |
| `display_name`, `avatar_preset`, `avatar_photo_id` | varchar(60), varchar(20), uuid | só para perfis sem conta, que têm nome e **exatamente um** avatar; quem tem conta deixa os três nulos (`ck_group_members_profile_fields`); `avatar_photo_id` → `avatars.id` |
| `role` | varchar(20) | `member`, `admin`, `owner`; dono tem de ter conta e perfil só é `member` (`ck_group_members_owner_has_account`, `ck_group_members_profile_is_member`) |
| `status` | varchar(20) | `active`, `left`, `removed`; `left_at` preenchido **se e somente se** não for `active` (`ck_group_members_left_at`) |
| `joined_at`, `left_at`, `updated_at` | timestamptz | |
| `version` | int | concorrência otimista (duas pessoas assumindo o mesmo perfil, duas trocas de papel) |

Índices que fazem parte das regras (não só desempenho):

- `ux_group_members_one_active_owner`: **único** em `(group_id)` onde `role = 'owner' AND status = 'active'`, no máximo um dono ativo por grupo. A transferência de propriedade rebaixa o dono atual e depois promove o novo, em duas etapas numa transação.
- `ux_group_members_group_user`: **único** em `(group_id, user_id)` onde `user_id IS NOT NULL`, uma linha por pessoa e grupo (perfis sem conta podem se repetir).
- `ix_group_members_user_active` (`user_id` onde ativo) para "meus grupos"; `ix_group_members_group_id` para listar os membros.

### Partidas (ADR-0007)

O estado do jogo é um `jsonb` opaco para a plataforma (só o módulo do jogo o entende). Quando a partida some (o grupo é apagado de verdade), tudo o que depende dela vai junto (`ON DELETE CASCADE`); as linhas de membros nunca são apagadas, então o histórico continua apontando para eles.

**`app.game_sessions`**

| Coluna | Tipo | Regras |
|---|---|---|
| `id` | uuid | PK |
| `group_id` | uuid | FK `groups.id` (cascade) |
| `game_id` | varchar(40) | slug do jogo (`mimica`); o catálogo vive no código |
| `rules_version` | int | versão das regras do jogo quando a partida foi criada |
| `host_member_id` | uuid | FK `group_members.id` (restrict): o anfitrião |
| `status` | varchar(20) | `waiting`, `in_progress`, `finished`, `cancelled` (`ck_game_sessions_status`) |
| `config` | jsonb | configuração validada e normalizada pelo jogo |
| `state`, `state_schema_version` | jsonb, int | nulos até começar; **juntos** (`ck_game_sessions_state_pair`) e obrigatórios em `in_progress`/`finished` (`ck_game_sessions_state_present`) |
| `version` | int | token de concorrência: sobe a cada mudança (duas ações simultâneas não gravam as duas) |
| `last_event_seq` | int | último número de sequência de evento usado |
| `rematch_of_id` | uuid, nulo | FK para a própria tabela (`SET NULL`): a partida original de uma revanche |
| `created_at`, `started_at`, `finished_at`, `cancelled_at`, `updated_at` | timestamptz | |

Índices: `(group_id, created_at desc)` e parcial `(group_id)` onde `status IN ('waiting','in_progress')` (partidas abertas).

**`app.game_session_players`**: quem joga. Sempre um **membro do grupo** (`member_id` → `group_members.id`, restrict). `team_no` (0 a 15, nulo sem time), `seat` (ordem de entrada, nunca reaproveitada), `status` (`joined`/`left`/`removed`; `left_at` preenchido se e somente se não for `joined`). **UNIQUE `(session_id, member_id)`**: sair e voltar reativa a mesma linha.

**`app.game_events`**: trilha. `id bigint identity`, `session_id`, `seq` (≥ 1), `type`, `actor_player_id`, `client_action_id`, `payload` jsonb (só fatos públicos), `created_at`. **UNIQUE `(session_id, seq)`** (sequência contínua e sem repetição) e **UNIQUE parcial `(session_id, client_action_id)`** onde não nulo: é isto que torna uma ação **idempotente**.

**`app.score_entries`**: livro-razão da pontuação (só o servidor escreve). `id bigint identity`, `session_id`, `player_id` e/ou `team_no` (**ao menos um**: `ck_score_entries_target`), `points`, `reason`, `event_seq` (o evento que gerou os pontos). O total de cada jogador e time é a soma das entradas.

**`app.session_results`**: classificação final de cada jogador numa partida encerrada, base dos rankings. PK `(session_id, player_id)`; `member_id`, `group_id` e `game_id` desnormalizados para ranking rápido; `team_no`, `rank` (≥ 1), `score`, `is_winner`, `finished_at`. Índice `(group_id, game_id, member_id)`.

> As demais tabelas entram junto com cada funcionalidade. Retenção da trilha (`game_events` antigos) e encerramento automático de partidas abandonadas ainda não existem.

## Conexão

- **Use o pooler do Supabase, nunca a conexão direta.** A string direta (`db.<ref>.supabase.co`) resolve só para IPv6 e não funciona no Render nem no GitHub Actions. Use o modo *session* do pooler (porta 5432, `aws-0-<região>.pooler.supabase.com`, usuário `<role>.<project_ref>`).
- O banco do plano gratuito aceita **60 conexões** no total, e boa parte é do próprio Supabase: mantenha `Maximum Pool Size=10`.
- Modelo da string (sem valores reais): ver `.env.example`.

## Ambiente local

Sem Docker, usando os binários de um PostgreSQL já instalado:

```powershell
./scripts/dev-db.ps1          # sobe um cluster privado em .tools/ (porta 54329)
./scripts/dev-db.ps1 stop
./scripts/dev-db.ps1 reset    # apaga e recria do zero
```

Com Docker: `docker compose up -d db` (mesma porta e mesma conexão).

Conexão de desenvolvimento (já é o padrão em `appsettings.Development.json`):
`Host=localhost;Port=54329;Database=ronat_dev;Username=postgres;Password=postgres`.

**Testes de integração** criam um banco descartável por classe de teste no servidor de `TEST_DATABASE_URL`
(padrão: `Host=localhost;Port=54329;Username=postgres;Password=postgres`), aplicam as migrações e o removem ao final.
PostgreSQL real, não InMemory/SQLite, para ter o mesmo comportamento da produção.

## Migrações

As ferramentas do EF são locais ao repositório (`dotnet tool restore`).

```bash
# criar uma migração (uma por PR)
dotnet ef migrations add NomeDaMigracao \
  --project src/RonatIa.Games.Infrastructure --startup-project src/RonatIa.Games.Api \
  --output-dir Persistence/Migrations

# aplicar no banco local
dotnet ef database update --project src/RonatIa.Games.Infrastructure --startup-project src/RonatIa.Games.Api

# aplicar em outro banco (ex.: o pooler do Supabase): a conexão vem da variável de ambiente
ConnectionStrings__Default='<string do pooler>' dotnet ef database update \
  --project src/RonatIa.Games.Infrastructure --startup-project src/RonatIa.Games.Api

# gerar um script SQL idempotente (para aplicar à mão)
dotnet ef migrations script --idempotent -o migrar.sql \
  --project src/RonatIa.Games.Infrastructure --startup-project src/RonatIa.Games.Api
```

Regras:

- Toda migração que cria tabelas termina com `migrationBuilder.EnableRowLevelSecurityOnAllTables("app");`.
- Conflito no `AppDbContextModelSnapshot` depois de um `rebase`: descarte a migração local e gere de novo.
- Em produção as migrações são aplicadas por um passo explícito (nunca automaticamente na subida da API).
- Seeds devem ser idempotentes.
