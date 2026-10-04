using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace RonatIa.Games.Api.Startup;

public static class ApiCors
{
    public const string PolicyName = "frontend";

    /// <summary>
    /// CORS por lista explícita de origens (nunca <c>*</c>). A configuração é lida de forma preguiçosa, pelo padrão de options.
    /// Credenciais são permitidas porque o SignalR (WebSocket) as exige quando a origem é diferente.
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services)
    {
        services.AddCors();

        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((cors, settings) =>
        {
            var exact = settings.Value.AllowedOrigins
                .Select(origin => origin.Trim().TrimEnd('/'))
                .Where(origin => origin.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var patterns = settings.Value.AllowedOriginPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                .ToArray();

            cors.AddPolicy(PolicyName, policy => policy
                .SetIsOriginAllowed(origin => exact.Contains(origin) || patterns.Any(pattern => pattern.IsMatch(origin)))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("X-Trace-Id", "Retry-After")
                .SetPreflightMaxAge(TimeSpan.FromHours(1)));
        });

        return services;
    }
}
