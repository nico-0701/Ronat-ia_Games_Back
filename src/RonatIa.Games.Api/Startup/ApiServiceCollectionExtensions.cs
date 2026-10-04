using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using RonatIa.Games.Api.Startup.Errors;

namespace RonatIa.Games.Api.Startup;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Registra tudo o que é específico da camada HTTP: controllers, erros, CORS, limites, OpenAPI e saúde.</summary>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClientSettings>(configuration.GetSection("Client"));
        services.Configure<DocsSettings>(configuration.GetSection("Docs"));
        services.Configure<CorsSettings>(configuration.GetSection("Cors"));

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddApiCors();
        services.AddApiRateLimiting();
        services.AddApiOpenApi();

        services.AddHealthChecks();

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
