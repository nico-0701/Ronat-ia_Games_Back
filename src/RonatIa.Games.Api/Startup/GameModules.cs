using RonatIa.Games.Abstractions;
using RonatIa.Games.Mimica;

namespace RonatIa.Games.Api.Startup;

public static class GameModules
{
    /// <summary>
    /// Instala os jogos do produto: cada módulo (<c>IGameModule</c>) é registrado aqui, como singleton, e passa a aparecer em
    /// <c>GET /api/v1/games</c>. Veja <c>docs/GAME_DEVELOPMENT.md</c>. Os testes registram, além destes, jogos próprios para
    /// exercitar o motor.
    /// </summary>
    public static IServiceCollection AddGameModules(this IServiceCollection services)
    {
        services.AddSingleton<IGameModule>(new MimicaGame());
        return services;
    }
}
