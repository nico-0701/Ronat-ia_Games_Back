using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Domain.Sessions;

/// <summary>
/// Uma partida de um jogo, dentro de um grupo. A plataforma guarda o ciclo de vida (lobby, em andamento, encerrada) e o
/// estado do jogo como um JSON opaco, que só o módulo do jogo interpreta. <see cref="Version"/> sobe a cada mudança: é o
/// token de concorrência (duas ações simultâneas não gravam as duas) e o que os clientes usam para saber se há novidade.
/// </summary>
public sealed class GameSession
{
    private GameSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid GroupId { get; private set; }

    /// <summary>Slug do jogo (ex.: <c>mimica</c>); o catálogo vive no código.</summary>
    public string GameId { get; private set; } = string.Empty;

    /// <summary>Versão das regras do jogo quando a partida foi criada.</summary>
    public int RulesVersion { get; private set; }

    /// <summary>O membro do grupo que criou e gerencia a partida (administradores do grupo também gerenciam).</summary>
    public Guid HostMemberId { get; private set; }

    public SessionStatus Status { get; private set; }

    /// <summary>Configuração validada e normalizada pelo módulo (JSON).</summary>
    public string ConfigJson { get; private set; } = "{}";

    /// <summary>Estado do jogo (JSON do módulo); nulo até a partida começar.</summary>
    public string? StateJson { get; private set; }

    public int? StateSchemaVersion { get; private set; }

    public int Version { get; private set; }

    /// <summary>Último número de sequência de evento usado nesta partida.</summary>
    public int LastEventSeq { get; private set; }

    /// <summary>Se esta partida é uma revanche, a partida original.</summary>
    public Guid? RematchOfId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status is SessionStatus.Waiting or SessionStatus.InProgress;

    public static GameSession Create(
        Guid groupId,
        string gameId,
        int rulesVersion,
        Guid hostMemberId,
        string configJson,
        DateTimeOffset now,
        Guid? rematchOfId = null) => new()
        {
            Id = Guid.CreateVersion7(now),
            GroupId = groupId,
            GameId = gameId,
            RulesVersion = rulesVersion,
            HostMemberId = hostMemberId,
            Status = SessionStatus.Waiting,
            ConfigJson = configJson,
            RematchOfId = rematchOfId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void UpdateConfig(string configJson, DateTimeOffset now)
    {
        RequireStatus(SessionStatus.Waiting, "session.not_waiting", "A configuração só muda enquanto a partida está no lobby.");
        ConfigJson = configJson;
        Touch(now);
    }

    /// <summary>Muda o lobby (jogadores, times): sobe a versão para os clientes perceberem.</summary>
    public void LobbyChanged(DateTimeOffset now)
    {
        RequireStatus(SessionStatus.Waiting, "session.not_waiting", "Isso só pode ser feito enquanto a partida está no lobby.");
        Touch(now);
    }

    public void Start(string stateJson, int schemaVersion, int eventsAdded, DateTimeOffset now)
    {
        RequireStatus(SessionStatus.Waiting, "session.not_waiting", "A partida já começou ou terminou.");
        Status = SessionStatus.InProgress;
        StateJson = stateJson;
        StateSchemaVersion = schemaVersion;
        LastEventSeq += eventsAdded;
        StartedAt = now;
        Touch(now);
    }

    /// <summary>Grava o novo estado depois de uma ação.</summary>
    public void Advance(string stateJson, int schemaVersion, int eventsAdded, DateTimeOffset now)
    {
        RequireStatus(SessionStatus.InProgress, "session.not_in_progress", "A partida não está em andamento.");
        StateJson = stateJson;
        StateSchemaVersion = schemaVersion;
        LastEventSeq += eventsAdded;
        Touch(now);
    }

    public void Finish(int eventsAdded, DateTimeOffset now)
    {
        RequireStatus(SessionStatus.InProgress, "session.not_in_progress", "A partida não está em andamento.");
        Status = SessionStatus.Finished;
        LastEventSeq += eventsAdded;
        FinishedAt = now;
        Touch(now);
    }

    /// <summary>Cancela uma partida no lobby ou em andamento (uma encerrada não volta atrás).</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw AppException.Conflict("session.already_ended", "A partida já terminou.");
        }

        Status = SessionStatus.Cancelled;
        CancelledAt = now;
        Touch(now);
    }

    private void RequireStatus(SessionStatus expected, string code, string message)
    {
        if (Status != expected)
        {
            throw AppException.Conflict(code, message);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
