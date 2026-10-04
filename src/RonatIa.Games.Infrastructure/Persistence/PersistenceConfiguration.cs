using Microsoft.EntityFrameworkCore;

namespace RonatIa.Games.Infrastructure.Persistence;

/// <summary>Configuração única do EF Core, compartilhada pela aplicação, pelas ferramentas de migração e pelos testes.</summary>
public static class PersistenceConfiguration
{
    /// <summary>Schema das tabelas da plataforma. O schema <c>public</c> do Supabase fica vazio de propósito (ver docs/DATABASE.md).</summary>
    public const string Schema = "app";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
        .UseSnakeCaseNamingConvention();
}
