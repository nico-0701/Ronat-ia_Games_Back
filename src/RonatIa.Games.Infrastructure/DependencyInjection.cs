using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RonatIa.Games.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra persistência, segurança e demais integrações externas.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
