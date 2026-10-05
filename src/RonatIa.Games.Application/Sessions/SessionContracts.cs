using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Sessions;

public sealed record CreateSessionRequest
{
    [Required]
    public Guid? GroupId { get; init; }

    /// <summary>Slug do jogo (veja <c>GET /games</c>), ex.: <c>mimica</c>.</summary>
    [Required, StringLength(40)]
    public string GameId { get; init; } = string.Empty;

    /// <summary>Configuração do jogo (um objeto JSON); o que faltar assume o padrão. Quem valida é o módulo do jogo.</summary>
    public JsonElement? Config { get; init; }
}

public sealed record UpdateSessionConfigRequest
{
    [Required]
    public JsonElement? Config { get; init; }
}

public sealed record AddSessionPlayerRequest
{
    /// <summary>O membro do grupo (com conta ou perfil sem conta) que vai jogar.</summary>
    [Required]
    public Guid? MemberId { get; init; }
}

public sealed record TeamAssignmentRequest
{
    [Required]
    public Guid? PlayerId { get; init; }

    /// <summary>Número do time (0, 1...) ou nulo para deixar sem time.</summary>
    public int? Team { get; init; }
}

public sealed record AssignTeamsRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public List<TeamAssignmentRequest> Assignments { get; init; } = [];
}

public sealed record GameActionRequest
{
    /// <summary>Identificador da ação, gerado pelo cliente. Reenviar o mesmo (ex.: depois de uma falha de rede) não reaplica a ação.</summary>
    [Required]
    public Guid? ClientActionId { get; init; }

    /// <summary>Tipo da ação, definido pelo jogo (veja <c>allowedActions</c> na visão do jogador).</summary>
    [Required, StringLength(60)]
    public string Type { get; init; } = string.Empty;

    public JsonElement? Payload { get; init; }
}

/// <param name="ConfigDefaults">A configuração padrão (um objeto JSON) para montar a tela de opções.</param>
/// <param name="TeamCount">0 = cada um por si; N = N times.</param>
public sealed record GameDto(
    string Id,
    string Name,
    string Description,
    int RulesVersion,
    int MinPlayers,
    int MaxPlayers,
    int TeamCount,
    int MinPlayersPerTeam,
    JsonElement ConfigDefaults);

/// <param name="Id">Identifica o jogador na partida (não é o id da conta nem o do membro).</param>
/// <param name="MemberId">O membro do grupo que joga.</param>
/// <param name="Score">Pontos do jogador (a soma do livro-razão).</param>
public sealed record SessionPlayerDto(Guid Id, Guid MemberId, string DisplayName, AvatarDto Avatar, bool HasAccount, int? Team, int Seat, int Score, bool IsMe);

public sealed record TeamScoreDto(int Team, int Score);

public sealed record StandingDto(Guid PlayerId, Guid MemberId, string DisplayName, int? Team, int Rank, int Score, bool IsWinner);

public sealed record GameSessionSummaryDto(
    Guid Id,
    string GameId,
    SessionStatus Status,
    Guid HostMemberId,
    int PlayerCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// A partida como <b>quem consultou</b> a enxerga: o lobby, os placares e a <paramref name="View"/> própria do jogo, que só
/// traz o que esta pessoa pode ver (segredos de outros ficam de fora).
/// </summary>
/// <param name="Version">Sobe a cada mudança; serve para saber se há novidade.</param>
/// <param name="CanManage">Anfitrião da partida ou administrador do grupo: pode configurar, iniciar, cancelar e encerrar.</param>
/// <param name="MyPlayerId">O jogador de quem consultou, ou nulo se só assiste.</param>
/// <param name="View">A visão do jogo para esta pessoa (nula enquanto a partida não começou).</param>
/// <param name="AllowedActions">As ações que esta pessoa pode enviar agora, calculadas pelo servidor.</param>
/// <param name="DeadlineAt">O prazo da fase atual, quando houver.</param>
/// <param name="Standings">A classificação final (vazia até a partida terminar).</param>
public sealed record GameSessionDto(
    Guid Id,
    Guid GroupId,
    string GameId,
    int RulesVersion,
    SessionStatus Status,
    int Version,
    Guid HostMemberId,
    bool CanManage,
    Guid? MyPlayerId,
    JsonElement Config,
    IReadOnlyList<SessionPlayerDto> Players,
    IReadOnlyList<TeamScoreDto> TeamScores,
    JsonElement? View,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset? DeadlineAt,
    IReadOnlyList<StandingDto> Standings,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <param name="Replayed">Verdadeiro quando o <c>clientActionId</c> já tinha sido aplicado: nada mudou, e <c>session</c> é o estado atual.</param>
public sealed record ActionResponse(GameSessionDto Session, bool Replayed);

/// <summary>Um evento da trilha da partida. Só traz fatos públicos: segredos nunca entram.</summary>
public sealed record EventDto(int Seq, string Type, Guid? ActorPlayerId, JsonElement? Payload, DateTimeOffset CreatedAt);
