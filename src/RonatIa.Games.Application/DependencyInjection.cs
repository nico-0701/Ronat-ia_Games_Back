using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Games;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Maintenance;
using RonatIa.Games.Application.Ranking;
using RonatIa.Games.Application.Sessions;
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

        services.AddScoped<GroupAccess>();
        services.AddScoped<GroupService>();
        services.AddScoped<GroupMemberService>();

        // Jogos: os módulos se registram como IGameModule; o catálogo e o sorteio são da plataforma.
        services.TryAddSingleton<IGameRandom, SystemGameRandom>();
        services.AddSingleton<IGameCatalog, GameCatalog>();
        services.AddScoped<SessionReader>();
        services.AddScoped<SessionLobbyService>();
        services.AddScoped<SessionPlayService>();
        services.AddScoped<RankingService>();
        services.AddScoped<MaintenanceService>();

        return services;
    }
}
