using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RonatIa.Games.Api.Realtime;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Controllers;

/// <summary>
/// Partidas de um grupo: lobby, times, configuração e jogo. Todas as rotas exigem login e que a pessoa seja membro do grupo da
/// partida (senão, 404). Quem gerencia é o anfitrião (quem criou) ou um administrador do grupo. O estado do jogo só chega a
/// cada pessoa pela <c>view</c> própria dela: segredos de outros jogadores nunca saem do servidor.
/// </summary>
[ApiController]
[Route("api/v1")]
[NotifySessionChanged]
public sealed class SessionsController(SessionLobbyService lobby, SessionPlayService play) : ControllerBase
{
    /// <summary>Cria uma partida no lobby. Quem cria vira o anfitrião e já entra como jogador.</summary>
    [HttpPost("sessions")]
    [EnableRateLimiting(RateLimitPolicies.SessionCreate)]
    [ProducesResponseType<GameSessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await lobby.CreateAsync(User.RequireUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { sessionId = session.Id }, session);
    }

    /// <summary>As partidas de um grupo (as abertas primeiro, depois as mais recentes).</summary>
    [HttpGet("groups/{groupId:guid}/sessions")]
    public async Task<ActionResult<IReadOnlyList<GameSessionSummaryDto>>> List(Guid groupId, [FromQuery] int limit = 30, CancellationToken cancellationToken = default) =>
        Ok(await lobby.ListAsync(User.RequireUserId(), groupId, limit, cancellationToken));

    /// <summary>A partida como quem consulta a enxerga (lobby, placares e a visão do jogo para esta pessoa).</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<GameSessionDto>> Get(Guid sessionId, CancellationToken cancellationToken) =>
        await lobby.GetAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>Entra no lobby como a própria pessoa (idempotente).</summary>
    [HttpPost("sessions/{sessionId:guid}/join")]
    public async Task<ActionResult<GameSessionDto>> Join(Guid sessionId, CancellationToken cancellationToken) =>
        await lobby.JoinAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>Sai do lobby.</summary>
    [HttpPost("sessions/{sessionId:guid}/leave")]
    public async Task<ActionResult<GameSessionDto>> Leave(Guid sessionId, CancellationToken cancellationToken) =>
        await lobby.LeaveAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>O anfitrião adiciona um membro do grupo (inclusive um perfil sem conta).</summary>
    [HttpPost("sessions/{sessionId:guid}/players")]
    public async Task<ActionResult<GameSessionDto>> AddPlayer(Guid sessionId, [FromBody] AddSessionPlayerRequest request, CancellationToken cancellationToken) =>
        await lobby.AddPlayerAsync(User.RequireUserId(), sessionId, request, cancellationToken);

    /// <summary>O anfitrião remove um jogador do lobby.</summary>
    [HttpDelete("sessions/{sessionId:guid}/players/{playerId:guid}")]
    public async Task<ActionResult<GameSessionDto>> RemovePlayer(Guid sessionId, Guid playerId, CancellationToken cancellationToken) =>
        await lobby.RemovePlayerAsync(User.RequireUserId(), sessionId, playerId, cancellationToken);

    /// <summary>Define o time de cada jogador informado (jogos com times, no lobby).</summary>
    [HttpPut("sessions/{sessionId:guid}/teams")]
    public async Task<ActionResult<GameSessionDto>> AssignTeams(Guid sessionId, [FromBody] AssignTeamsRequest request, CancellationToken cancellationToken) =>
        await lobby.AssignTeamsAsync(User.RequireUserId(), sessionId, request, cancellationToken);

    /// <summary>Sorteia os times de forma equilibrada.</summary>
    [HttpPost("sessions/{sessionId:guid}/teams/shuffle")]
    public async Task<ActionResult<GameSessionDto>> ShuffleTeams(Guid sessionId, CancellationToken cancellationToken) =>
        await lobby.ShuffleTeamsAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>Altera a configuração do jogo (só no lobby). O servidor valida e normaliza.</summary>
    [HttpPatch("sessions/{sessionId:guid}/config")]
    public async Task<ActionResult<GameSessionDto>> UpdateConfig(Guid sessionId, [FromBody] UpdateSessionConfigRequest request, CancellationToken cancellationToken) =>
        await lobby.UpdateConfigAsync(User.RequireUserId(), sessionId, request, cancellationToken);

    /// <summary>Começa a partida: confere jogadores e times e pede ao jogo o estado inicial.</summary>
    [HttpPost("sessions/{sessionId:guid}/start")]
    public async Task<ActionResult<GameSessionDto>> Start(Guid sessionId, CancellationToken cancellationToken) =>
        await play.StartAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>Cancela a partida (no lobby ou em andamento).</summary>
    [HttpPost("sessions/{sessionId:guid}/cancel")]
    public async Task<ActionResult<GameSessionDto>> Cancel(Guid sessionId, CancellationToken cancellationToken) =>
        await lobby.CancelAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>Encerra a partida antes do fim natural; o placar do momento vira o resultado.</summary>
    [HttpPost("sessions/{sessionId:guid}/finish")]
    public async Task<ActionResult<GameSessionDto>> Finish(Guid sessionId, CancellationToken cancellationToken) =>
        await play.FinishAsync(User.RequireUserId(), sessionId, cancellationToken);

    /// <summary>
    /// Envia uma ação de jogo. O servidor valida (quem pode, em que fase, dentro do prazo), aplica as regras e calcula os pontos:
    /// o cliente nunca envia pontuação. <c>clientActionId</c> torna o envio idempotente (<c>replayed: true</c> quando já tinha sido aplicado).
    /// Recusas do jogo: <c>400</c> (ação malformada), <c>403</c> (não é a sua vez/papel) ou <c>409</c> (fase ou prazo).
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/actions")]
    [EnableRateLimiting(RateLimitPolicies.SessionAction)]
    public async Task<ActionResult<ActionResponse>> Apply(Guid sessionId, [FromBody] GameActionRequest request, CancellationToken cancellationToken) =>
        await play.ApplyAsync(User.RequireUserId(), sessionId, request, cancellationToken);

    /// <summary>A trilha da partida (só fatos públicos), a partir de uma sequência.</summary>
    [HttpGet("sessions/{sessionId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<EventDto>>> Events(Guid sessionId, [FromQuery] int after = 0, [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await play.EventsAsync(User.RequireUserId(), sessionId, after, limit, cancellationToken));

    /// <summary>Uma partida nova no lobby, com o mesmo jogo, configuração e jogadores. Só depois que a anterior termina ou é cancelada.</summary>
    [HttpPost("sessions/{sessionId:guid}/rematch")]
    [ProducesResponseType<GameSessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Rematch(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await lobby.RematchAsync(User.RequireUserId(), sessionId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { sessionId = session.Id }, session);
    }
}
