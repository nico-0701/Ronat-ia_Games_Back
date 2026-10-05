namespace RonatIa.Games.Domain.Sessions;

/// <summary>
/// Trilha da partida: um evento por ação aplicada (com o <see cref="ClientActionId"/>, que garante a idempotência) e um por
/// consequência que o módulo informa. O <see cref="PayloadJson"/> carrega ids e fatos públicos, <b>nunca</b> um segredo.
/// </summary>
public sealed class GameEventRecord
{
    private GameEventRecord()
    {
    }

    public long Id { get; private set; }

    public Guid SessionId { get; private set; }

    /// <summary>Sequência dentro da partida, a partir de 1; é contínua e sem repetição.</summary>
    public int Seq { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public Guid? ActorPlayerId { get; private set; }

    /// <summary>Identificador da ação enviado pelo cliente; reenviar o mesmo não reaplica a ação.</summary>
    public Guid? ClientActionId { get; private set; }

    public string? PayloadJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static GameEventRecord Create(
        Guid sessionId,
        int seq,
        string type,
        Guid? actorPlayerId,
        Guid? clientActionId,
        string? payloadJson,
        DateTimeOffset now) => new()
        {
            SessionId = sessionId,
            Seq = seq,
            Type = type,
            ActorPlayerId = actorPlayerId,
            ClientActionId = clientActionId,
            PayloadJson = payloadJson,
            CreatedAt = now,
        };
}

/// <summary>Livro-razão da pontuação: só o servidor escreve, e o total de cada jogador e time é a soma das entradas.</summary>
public sealed class ScoreEntry
{
    private ScoreEntry()
    {
    }

    public long Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid? PlayerId { get; private set; }

    public int? TeamNo { get; private set; }

    public int Points { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>A sequência do evento que gerou estes pontos.</summary>
    public int EventSeq { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ScoreEntry Create(Guid sessionId, Guid? playerId, int? teamNo, int points, string reason, int eventSeq, DateTimeOffset now) => new()
    {
        SessionId = sessionId,
        PlayerId = playerId,
        TeamNo = teamNo,
        Points = points,
        Reason = reason,
        EventSeq = eventSeq,
        CreatedAt = now,
    };
}

/// <summary>Classificação final de cada jogador numa partida encerrada; é a base dos rankings do grupo.</summary>
public sealed class SessionResult
{
    private SessionResult()
    {
    }

    public Guid SessionId { get; private set; }

    public Guid PlayerId { get; private set; }

    /// <summary>O membro do grupo (é a ele que o histórico pertence, mesmo que a pessoa assuma o perfil depois).</summary>
    public Guid MemberId { get; private set; }

    public Guid GroupId { get; private set; }

    public string GameId { get; private set; } = string.Empty;

    public int? TeamNo { get; private set; }

    public int Rank { get; private set; }

    public int Score { get; private set; }

    public bool IsWinner { get; private set; }

    public DateTimeOffset FinishedAt { get; private set; }

    public static SessionResult Create(
        Guid sessionId,
        Guid playerId,
        Guid memberId,
        Guid groupId,
        string gameId,
        int? teamNo,
        int rank,
        int score,
        bool isWinner,
        DateTimeOffset finishedAt) => new()
        {
            SessionId = sessionId,
            PlayerId = playerId,
            MemberId = memberId,
            GroupId = groupId,
            GameId = gameId,
            TeamNo = teamNo,
            Rank = rank,
            Score = score,
            IsWinner = isWinner,
            FinishedAt = finishedAt,
        };
}
