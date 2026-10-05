namespace RonatIa.Games.Application.Options;

/// <summary>
/// Limpeza periódica (seção <c>Maintenance</c>). O plano gratuito tem 500 MB de banco e o servidor dorme; a limpeza só roda
/// enquanto a API está acordada, e a correção do jogo nunca depende dela.
/// </summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Liga a limpeza automática (um serviço em segundo plano).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>De quanto em quanto tempo roda.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Um lobby sem nenhuma mudança por este tempo é cancelado (senão ocuparia, para sempre, uma das partidas abertas do grupo).</summary>
    public TimeSpan AbandonedLobbyAfter { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Uma partida em andamento sem nenhuma ação por este tempo é cancelada.</summary>
    public TimeSpan AbandonedGameAfter { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Eventos de partidas encerradas há mais que isto são apagados (ficam o placar, o livro-razão e o resultado).</summary>
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(60);

    /// <summary>Sessões de login expiradas ou encerradas há mais que isto são apagadas.</summary>
    public TimeSpan LoginRetention { get; set; } = TimeSpan.FromDays(30);
}
