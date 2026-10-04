using Microsoft.EntityFrameworkCore;
using Npgsql;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>
/// Banco PostgreSQL descartável para os testes de integração: cria um banco novo no servidor indicado por
/// <c>TEST_DATABASE_URL</c> (padrão: o banco local do scripts/dev-db.ps1 ou do docker-compose), aplica as migrações
/// de verdade e remove tudo ao final. Usar PostgreSQL real (e não InMemory/SQLite) garante o mesmo comportamento da produção.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private const string DefaultServer = "Host=localhost;Port=54329;Username=postgres;Password=postgres";

    private readonly string _adminConnectionString;
    private readonly string _name;
    private int _disposed;

    private TestDatabase(string adminConnectionString, string name, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static TestDatabase Create()
    {
        var server = Environment.GetEnvironmentVariable("TEST_DATABASE_URL") is { Length: > 0 } url ? url : DefaultServer;
        var admin = new NpgsqlConnectionStringBuilder(server) { Database = "postgres", Pooling = false, Timeout = 10 };
        var name = $"ronat_test_{Guid.NewGuid():N}";

        try
        {
            using var connection = new NpgsqlConnection(admin.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            command.ExecuteNonQuery();
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "Banco de testes indisponível. Suba um PostgreSQL local com ./scripts/dev-db.ps1 (ou docker compose up -d db) " +
                "ou defina TEST_DATABASE_URL.",
                exception);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = name, MaxPoolSize = 20 }.ConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>();
        PersistenceConfiguration.Configure(options, connectionString);
        using (var context = new AppDbContext(options.Options))
        {
            context.Database.Migrate();
        }

        return new TestDatabase(admin.ConnectionString, name, connectionString);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            using (var pooled = new NpgsqlConnection(ConnectionString))
            {
                NpgsqlConnection.ClearPool(pooled);
            }

            using var connection = new NpgsqlConnection(_adminConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
            command.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Limpeza de melhor esforço: um banco de teste sobrando não deve derrubar a suíte.
        }
    }
}
