using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using RonatIa.Games.Api.Startup.Errors;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Startup;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Registra tudo o que é específico da camada HTTP: controllers, erros, CORS, limites, OpenAPI e saúde.</summary>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClientSettings>(configuration.GetSection("Client"));
        services.Configure<DocsSettings>(configuration.GetSection("Docs"));
        services.Configure<CorsSettings>(configuration.GetSection("Cors"));
        services.Configure<RateLimitingSettings>(configuration.GetSection("RateLimiting"));

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

                // Contrato estrito: número é número (o padrão aceitaria "5" como 5, e o OpenAPI anunciaria "inteiro ou texto").
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            });

        // O gerador de OpenAPI lê as opções JSON "http" (e não as do MVC): mantê-las iguais evita contrato diferente do comportamento real.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddApiCors();
        services.AddApiAuthentication();
        services.AddApiRateLimiting();
        services.AddApiOpenApi();
        services.AddGameModules();

        // "ready" só fica verde se o banco responde; "live" não depende de nada externo.
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        // O app só é alcançável pelo proxy da hospedagem (Render), que define os cabeçalhos X-Forwarded-*.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        return services;
    }
}
