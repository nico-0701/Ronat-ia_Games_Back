using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Sobe a API em memória, ligada a um banco PostgreSQL próprio (criado e migrado para esta instância).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabase _database = TestDatabase.Create();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting vale desde o início da configuração (ConfigureAppConfiguration chegaria tarde para leituras do Program).
        builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Docs:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://app.exemplo.test");
        builder.UseSetting("Cors:AllowedOriginPatterns:0", @"^https://[a-z0-9-]+\.exemplo\.pages\.dev$");

        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly));
    }

    /// <summary>Executa uma ação com um <see cref="AppDbContext"/> novo (escopo próprio), útil para preparar e conferir dados.</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _database.Dispose();
    }
}
