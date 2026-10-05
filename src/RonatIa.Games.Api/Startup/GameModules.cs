namespace RonatIa.Games.Api.Startup;

public static class GameModules
{
    /// <summary>
    /// Instala os jogos do produto: cada módulo (<c>IGameModule</c>) é registrado aqui, como singleton, e passa a aparecer em
    /// <c>GET /api/v1/games</c>. Veja <c>docs/GAME_DEVELOPMENT.md</c>. Ainda não há jogos instalados (o primeiro, a Mímica, vem na
    /// fase seguinte); os testes registram jogos próprios para exercitar o motor.
    /// </summary>
    public static IServiceCollection AddGameModules(this IServiceCollection services)
    {
        // services.AddSingleton<IGameModule, MimicaGame>();
        return services;
    }
}
