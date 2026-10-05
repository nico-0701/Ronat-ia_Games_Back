namespace RonatIa.Games.Application.Options;

/// <summary>Limites das partidas (seção <c>Sessions</c>). Protegem o banco e a memória gratuitos.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>Partidas no lobby ou em andamento por grupo ao mesmo tempo.</summary>
    public int MaxActiveSessionsPerGroup { get; set; } = 5;

    /// <summary>Quantos eventos uma consulta devolve, no máximo.</summary>
    public int MaxEventsPerPage { get; set; } = 200;
}
