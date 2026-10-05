using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Sobe a API em memória, ligada a um banco PostgreSQL próprio (criado e migrado para esta instância).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabase _database = TestDatabase.Create();

    /// <summary>Segredos de teste (aleatórios por instância, mas estáveis entre os hosts derivados, que compartilham o mesmo banco).</summary>
    public string PhonePepper { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public string JwtSigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting vale desde o início da configuração (ConfigureAppConfiguration chegaria tarde para leituras do Program).
        builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
        builder.UseSetting("Auth:PhonePepper", PhonePepper);
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Docs:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://app.exemplo.test");
        builder.UseSetting("Cors:AllowedOriginPatterns:0", @"^https://[a-z0-9-]+\.exemplo\.pages\.dev$");

        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly);

            // Jogos de teste: o motor é exercitado sem depender de um jogo real.
            services.AddSingleton<IGameModule, RelayGame>();
            services.AddSingleton<IGameModule, SoloGame>();
        });
    }

    /// <summary>Host derivado (mesmo banco e mesmos segredos) com relógio controlável, para testes de expiração.</summary>
    public WebApplicationFactory<Program> WithFakeTime(FakeTimeProvider time) =>
        WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));

    /// <summary>Host derivado com configurações extras (ex.: modo de cadastro, limites).</summary>
    public WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

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
