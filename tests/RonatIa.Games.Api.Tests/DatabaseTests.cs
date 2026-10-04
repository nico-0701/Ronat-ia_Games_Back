using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Api.Tests;

public sealed class DatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Migrations_create_the_identity_tables_in_the_app_schema()
    {
        var tables = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'app' ORDER BY 1""")
            .ToListAsync());

        Assert.Contains("users", tables);
        Assert.Contains("avatars", tables);
        Assert.Contains("auth_sessions", tables);
        Assert.Contains("__ef_migrations_history", tables);
    }

    [Fact]
    public async Task The_public_schema_stays_empty()
    {
        // O schema "public" é exposto pela Data API do Supabase; nada nosso pode morar lá.
        var tables = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'public'""")
            .ToListAsync());

        Assert.Empty(tables);
    }

    [Fact]
    public async Task Every_table_in_the_app_schema_has_row_level_security_enabled()
    {
        var withoutRls = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT c.relname AS "Value"
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'app' AND c.relkind IN ('r', 'p') AND NOT c.relrowsecurity
                """)
            .ToListAsync());

        Assert.True(
            withoutRls.Count == 0,
            $"Tabelas sem RLS: {string.Join(", ", withoutRls)}. Chame migrationBuilder.EnableRowLevelSecurityOnAllTables(\"app\") na migração.");
    }

    [Fact]
    public async Task Phone_hash_must_be_unique()
    {
        var hash = RandomNumberGenerator.GetBytes(32);
        var now = DateTimeOffset.UtcNow;

        await factory.WithDbAsync(async db =>
        {
            db.Users.Add(User.Register(hash, "1234", "Primeira", null, "2026-10", now));
            await db.SaveChangesAsync();
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => factory.WithDbAsync(async db =>
        {
            db.Users.Add(User.Register(hash, "1234", "Segunda", null, "2026-10", now));
            await db.SaveChangesAsync();
        }));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_users_phone_hash", postgres.ConstraintName);
    }

    [Fact]
    public async Task A_user_must_have_exactly_one_avatar()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO app.users (id, phone_hash, phone_last4, display_name, avatar_preset, avatar_photo_id, status, is_admin, created_at, updated_at)
            VALUES (gen_random_uuid(), decode(repeat('ab', 32), 'hex'), '0000', 'Sem avatar', NULL, NULL, 'active', false, now(), now())
            """)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_users_avatar_one_of", exception.ConstraintName);
    }

    [Fact]
    public async Task User_status_is_stored_as_lowercase_text()
    {
        var hash = RandomNumberGenerator.GetBytes(32);
        var user = User.Register(hash, "9999", "Pessoa Teste", "preset-2", "2026-10", DateTimeOffset.UtcNow);

        await factory.WithDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });

        var raw = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT status AS "Value" FROM app.users WHERE id = {user.Id}""")
            .SingleAsync());
        var loaded = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));

        Assert.Equal("active", raw);
        Assert.Equal(UserStatus.Active, loaded.Status);
        Assert.Equal("preset-2", loaded.AvatarPreset);
    }

    [Fact]
    public async Task Ready_endpoint_is_healthy_when_the_database_responds()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_endpoint_is_unhealthy_when_the_database_is_unreachable()
    {
        var broken = factory.WithSettings(
            ("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2;Command Timeout=2"));

        var ready = await broken.CreateClient().GetAsync("/health/ready");
        var live = await broken.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
