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

> As demais tabelas (grupos, membros, partidas, eventos, pontuação, resultados) entram junto com cada funcionalidade, uma migração por PR.

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
