using Microsoft.AspNetCore.Mvc;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Ranking;

namespace RonatIa.Games.Api.Controllers;

/// <summary>
/// Ranking e histórico dos grupos, calculados das partidas encerradas. O histórico pertence ao <b>membro</b> do grupo
/// (com ou sem conta): quem assume um perfil sem conta herda o que ele já jogou. Quem não é do grupo recebe 404.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class RankingController(RankingService ranking) : ControllerBase
{
    /// <summary>
    /// Ranking do grupo: partidas, vitórias, aproveitamento e pontos de cada membro, ordenado por vitórias e aproveitamento.
    /// Filtre por jogo (<c>gameId</c>) e por janela de tempo (<c>period</c>: <c>all</c>, <c>year</c>, <c>quarter</c>, <c>month</c>, <c>week</c>).
    /// </summary>
    [HttpGet("groups/{groupId:guid}/ranking")]
    public async Task<ActionResult<RankingDto>> Ranking(Guid groupId, [FromQuery] string? gameId, [FromQuery] RankingPeriod period = RankingPeriod.All, CancellationToken cancellationToken = default) =>
        await ranking.RankingAsync(User.RequireUserId(), groupId, gameId, period, cancellationToken);

    /// <summary>
    /// As partidas encerradas do grupo, da mais recente para a mais antiga, com a classificação de cada uma. Paginação por cursor:
    /// passe <c>nextBefore</c> da resposta em <c>before</c> para a página seguinte.
    /// </summary>
    [HttpGet("groups/{groupId:guid}/history")]
    public async Task<ActionResult<HistoryPageDto>> History(Guid groupId, [FromQuery] string? gameId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 20, CancellationToken cancellationToken = default) =>
        await ranking.HistoryAsync(User.RequireUserId(), groupId, gameId, before, limit, cancellationToken);

    /// <summary>As estatísticas da própria pessoa, somando todos os grupos de que participa.</summary>
    [HttpGet("users/me/stats")]
    public async Task<ActionResult<MyStatsDto>> MyStats(CancellationToken cancellationToken) =>
        await ranking.MyStatsAsync(User.RequireUserId(), cancellationToken);
}
