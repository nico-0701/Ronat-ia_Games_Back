using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RonatIa.Games.Infrastructure.Persistence;

/// <summary>
/// Usada só pelas ferramentas do EF (<c>dotnet ef</c>). Lê <c>ConnectionStrings__Default</c> do ambiente; sem ela, usa o banco
/// local de desenvolvimento (scripts/dev-db.ps1). Para gerar uma migração a conexão nem é aberta.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDevelopmentConnection = "Host=localhost;Port=54329;Database=ronat_dev;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalDevelopmentConnection;

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        PersistenceConfiguration.Configure(builder, connectionString);
        return new AppDbContext(builder.Options);
    }
}
