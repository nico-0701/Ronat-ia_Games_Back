using RonatIa.Games.Application.Sessions;

namespace RonatIa.Games.Api.Realtime;

/// <summary>Nomes das mensagens que o servidor envia aos clientes (e que o cliente registra com <c>connection.on</c>).</summary>
public static class RealtimeEvents
{
    /// <summary>A partida mudou: carrega a <see cref="GameSessionDto"/> <b>própria de quem recebe</b> (segredos de outros ficam de fora).</summary>
    public const string SessionUpdated = "SessionUpdated";

    /// <summary>Alguém do grupo entrou ou saiu da tela da partida.</summary>
    public const string PresenceChanged = "PresenceChanged";

    /// <summary>Foi criada a revanche da partida que o cliente assiste.</summary>
    public const string RematchCreated = "RematchCreated";

    /// <summary>A pessoa deixou de ter acesso à partida (saiu ou foi removida do grupo); a assinatura foi encerrada.</summary>
    public const string AccessRevoked = "AccessRevoked";

    /// <summary>A lista de partidas do grupo mudou (criada, começou, terminou...): um aviso para recarregar <c>GET /groups/{id}/sessions</c>.</summary>
    public const string GroupSessionsChanged = "GroupSessionsChanged";
}

/// <summary>Resposta de <c>Subscribe</c>: a partida agora e quem já está na tela dela.</summary>
/// <param name="Session">A partida como quem assina a enxerga.</param>
/// <param name="OnlineMemberIds">Membros do grupo com a tela da partida aberta neste momento.</param>
public sealed record SubscribeResult(GameSessionDto Session, IReadOnlyList<Guid> OnlineMemberIds);

public sealed record PresenceDto(Guid SessionId, Guid MemberId, bool Online);

public sealed record RematchDto(Guid SessionId, Guid NewSessionId);

public sealed record AccessRevokedDto(Guid SessionId);

public sealed record GroupSessionsChangedDto(Guid GroupId);
