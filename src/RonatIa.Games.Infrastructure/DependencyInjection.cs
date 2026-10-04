using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra persistência, segurança e demais integrações externas.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A conexão é lida quando o DbContext é criado (e não aqui), para que configurações de teste e variáveis de ambiente valham.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("A string de conexão ConnectionStrings:Default não está configurada.");

            PersistenceConfiguration.Configure(options, connectionString);
        });

        services.AddScoped<IAppDbContext>(serviceProvider => serviceProvider.GetRequiredService<AppDbContext>());

        return services;
    }
}
