namespace RonatIa.Games.Api.Realtime;

/// <summary>
/// Quem está olhando cada partida (e cada lista de partidas de grupo) neste momento, em memória. Serve para saber a quem enviar
/// a visão de cada um e para a presença. Com uma única instância da API (o plano gratuito) basta; com mais de uma, seria
/// preciso um backplane. Tudo é protegido por um único <c>lock</c>: as listas são pequenas (alguns jogadores por partida).
/// </summary>
public sealed class SessionSubscriptions
{
    /// <summary>Máximo de partidas e grupos que uma mesma conexão pode assinar (contra abuso).</summary>
    public const int MaxPerConnection = 20;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Subscriber>> _bySession = [];
    private readonly Dictionary<Guid, Dictionary<string, Guid>> _byGroup = [];
    private readonly Dictionary<string, Connection> _connections = [];

    /// <param name="AuthSessionId">A sessão de login (aparelho) da conexão: se ela for encerrada, a conexão deixa de receber dados.</param>
    public sealed record Subscriber(string ConnectionId, Guid UserId, Guid MemberId, Guid AuthSessionId);

    /// <summary>O que uma conexão assinou: usado para limpar tudo quando ela cai.</summary>
    private sealed class Connection
    {
        public HashSet<Guid> Sessions { get; } = [];

        public HashSet<Guid> Groups { get; } = [];

        public int Count => Sessions.Count + Groups.Count;
    }

    /// <summary>Uma mudança de presença: o membro deixou de ter qualquer conexão na partida (ou passou a ter).</summary>
    public sealed record PresenceChange(Guid SessionId, Guid MemberId, bool Online);

    /// <summary>Assina a partida. Falso se a conexão já atingiu o limite. <paramref name="cameOnline"/> diz se o membro acabou de ficar online.</summary>
    public bool TryAdd(Guid sessionId, Subscriber subscriber, out bool cameOnline)
    {
        lock (_gate)
        {
            cameOnline = false;
            var connection = ConnectionOf(subscriber.ConnectionId);
            if (!connection.Sessions.Contains(sessionId) && connection.Count >= MaxPerConnection)
            {
                return false;
            }

            if (!_bySession.TryGetValue(sessionId, out var subscribers))
            {
                subscribers = [];
                _bySession[sessionId] = subscribers;
            }

            cameOnline = !subscribers.Values.Any(s => s.MemberId == subscriber.MemberId);
            if (subscribers.ContainsKey(subscriber.ConnectionId))
            {
                cameOnline = false; // a mesma conexão assinando de novo
            }

            subscribers[subscriber.ConnectionId] = subscriber;
            connection.Sessions.Add(sessionId);
            return true;
        }
    }

    /// <summary>Cancela a assinatura. Devolve a mudança de presença, se o membro deixou de estar online na partida.</summary>
    public PresenceChange? Remove(Guid sessionId, string connectionId)
    {
        lock (_gate)
        {
            return RemoveCore(sessionId, connectionId);
        }
    }

    public bool TryAddGroup(Guid groupId, string connectionId, Guid userId)
    {
        lock (_gate)
        {
            var connection = ConnectionOf(connectionId);
            if (!connection.Groups.Contains(groupId) && connection.Count >= MaxPerConnection)
            {
                return false;
            }

            if (!_byGroup.TryGetValue(groupId, out var subscribers))
            {
                subscribers = [];
                _byGroup[groupId] = subscribers;
            }

            subscribers[connectionId] = userId;
            connection.Groups.Add(groupId);
            return true;
        }
    }

    public void RemoveGroup(Guid groupId, string connectionId)
    {
        lock (_gate)
        {
            RemoveGroupCore(groupId, connectionId);
        }
    }

    /// <summary>A conexão caiu: remove todas as assinaturas e devolve as mudanças de presença que isso causou.</summary>
    public IReadOnlyList<PresenceChange> RemoveConnection(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                return [];
            }

            var changes = new List<PresenceChange>();
            foreach (var sessionId in connection.Sessions.ToList())
            {
                if (RemoveCore(sessionId, connectionId) is { } change)
                {
                    changes.Add(change);
                }
            }

            foreach (var groupId in connection.Groups.ToList())
            {
                RemoveGroupCore(groupId, connectionId);
            }

            _connections.Remove(connectionId);
            return changes;
        }
    }

    public IReadOnlyList<Subscriber> Subscribers(Guid sessionId)
    {
        lock (_gate)
        {
            return _bySession.TryGetValue(sessionId, out var subscribers) ? [.. subscribers.Values] : [];
        }
    }

    public IReadOnlyList<string> GroupConnections(Guid groupId)
    {
        lock (_gate)
        {
            return _byGroup.TryGetValue(groupId, out var subscribers) ? [.. subscribers.Keys] : [];
        }
    }

    public IReadOnlyList<Guid> OnlineMembers(Guid sessionId)
    {
        lock (_gate)
        {
            return _bySession.TryGetValue(sessionId, out var subscribers)
                ? subscribers.Values.Select(s => s.MemberId).Distinct().ToList()
                : [];
        }
    }

    private Connection ConnectionOf(string connectionId)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            connection = new Connection();
            _connections[connectionId] = connection;
        }

        return connection;
    }

    private PresenceChange? RemoveCore(Guid sessionId, string connectionId)
    {
        if (!_bySession.TryGetValue(sessionId, out var subscribers) || !subscribers.Remove(connectionId, out var removed))
        {
            return null;
        }

        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.Sessions.Remove(sessionId);
        }

        var wentOffline = !subscribers.Values.Any(s => s.MemberId == removed.MemberId);
        if (subscribers.Count == 0)
        {
            _bySession.Remove(sessionId);
        }

        return wentOffline ? new PresenceChange(sessionId, removed.MemberId, Online: false) : null;
    }

    private void RemoveGroupCore(Guid groupId, string connectionId)
    {
        if (_byGroup.TryGetValue(groupId, out var subscribers))
        {
            subscribers.Remove(connectionId);
            if (subscribers.Count == 0)
            {
                _byGroup.Remove(groupId);
            }
        }

        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.Groups.Remove(groupId);
        }
    }
}
