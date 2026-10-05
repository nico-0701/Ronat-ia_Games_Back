using Microsoft.AspNetCore.Mvc;
using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Controllers;

/// <summary>O catálogo de jogos instalados.</summary>
[ApiController]
[Route("api/v1/games")]
public sealed class GamesController(SessionLobbyService sessions) : ControllerBase
{
    /// <summary>Os jogos disponíveis, com limites de jogadores, times e a configuração padrão.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<GameDto>> List() => Ok(sessions.Games());

    /// <summary>Um jogo do catálogo.</summary>
    [HttpGet("{gameId}")]
    public ActionResult<GameDto> Get(string gameId) => sessions.Game(gameId);
}
