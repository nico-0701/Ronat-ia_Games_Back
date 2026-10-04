using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Sobe a API em memória para os testes de integração.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting vale desde o início da configuração (ConfigureAppConfiguration chegaria tarde para leituras do Program).
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Docs:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://app.exemplo.test");
        builder.UseSetting("Cors:AllowedOriginPatterns:0", @"^https://[a-z0-9-]+\.exemplo\.pages\.dev$");

        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly));
    }
}
