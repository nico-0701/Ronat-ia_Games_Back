using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Users;

namespace RonatIa.Games.Application;

public static class DependencyInjection
{
    /// <summary>Registra os serviços de aplicação (casos de uso).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<AvatarService>();

        return services;
    }
}
