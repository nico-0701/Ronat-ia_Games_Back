using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Application.Ranking;

/// <summary>Janela de tempo do ranking, contada de agora para trás.</summary>
public enum RankingPeriod
{
    /// <summary>Desde sempre.</summary>
    All,

    /// <summary>Últimos 365 dias.</summary>
    Year,

    /// <summary>Últimos 90 dias.</summary>
    Quarter,

    /// <summary>Últimos 30 dias.</summary>
    Month,

    /// <summary>Últimos 7 dias.</summary>
    Week,
}

/// <param name="Rank">Posição: empate em vitórias, aproveitamento e partidas divide a mesma posição.</param>
/// <param name="MemberId">O membro do grupo (com ou sem conta); o histórico segue o membro, inclusive se um perfil sem conta for assumido depois.</param>
/// <param name="Played">Partidas encerradas de que o membro participou.</param>
/// <param name="Wins">Partidas em que venceu (num empate, todos vencem).</param>
/// <param name="WinRate">Vitórias ÷ partidas, de 0 a 1.</param>
/// <param name="Score">Soma dos pontos do membro (nos jogos de times, os do time dele).</param>
public sealed record RankingEntryDto(
    int Rank,
    Guid MemberId,
    string DisplayName,
    AvatarDto Avatar,
    bool HasAccount,
    bool IsMe,
    int Played,
    int Wins,
    double WinRate,
    int Score);

/// <param name="GameId">O jogo filtrado, ou nulo para todos.</param>
/// <param name="Since">O início da janela, ou nulo para "desde sempre".</param>
public sealed record RankingDto(string? GameId, RankingPeriod Period, DateTimeOffset? Since, IReadOnlyList<RankingEntryDto> Entries);

public sealed record HistoryStandingDto(Guid MemberId, string DisplayName, AvatarDto Avatar, int? Team, int Rank, int Score, bool IsWinner);

public sealed record HistoryEntryDto(Guid SessionId, string GameId, DateTimeOffset? StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<HistoryStandingDto> Standings);

/// <param name="NextBefore">Se houver mais, passe este valor em <c>before</c> para buscar a página seguinte (mais antiga).</param>
public sealed record HistoryPageDto(IReadOnlyList<HistoryEntryDto> Items, DateTimeOffset? NextBefore);

public sealed record GameStatsDto(string GameId, int Played, int Wins, int Score);

/// <summary>As estatísticas de uma pessoa somando todos os grupos de que ela participa.</summary>
public sealed record MyStatsDto(int Played, int Wins, int Groups, IReadOnlyList<GameStatsDto> ByGame);
