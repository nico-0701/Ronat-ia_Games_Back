using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Sessions;

/// <summary>
/// Jogar: começar, aplicar ações, encerrar e consultar a trilha. A plataforma cuida do que é comum a todo jogo (quem pode
/// agir, versão e concorrência, idempotência, eventos, livro-razão e resultado) e delega as regras ao módulo, que é puro.
/// </summary>
/// <remarks>
/// Cada ação roda em <b>uma única gravação</b>: novo estado + eventos + pontos (+ resultado, se acabou). A versão da partida é o
/// token de concorrência: se duas ações chegam ao mesmo tempo, a segunda é reavaliada sobre o estado novo (e pode ser
/// recusada pelo módulo) ou, persistindo o conflito, responde <c>409</c>. Repetir um <c>clientActionId</c> devolve o estado atual
/// sem reaplicar a ação.
/// </remarks>
public sealed class SessionPlayService(
    IAppDbContext db,
    SessionReader reader,
    IDbExceptionClassifier dbErrors,
    IOptions<SessionOptions> options,
    TimeProvider time)
{
    private const int MaxAttempts = 6;

    /// <summary>Começa a partida: confere jogadores e times, pede o estado inicial ao módulo e abre a trilha.</summary>
    public Task<GameSessionDto> StartAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        RetryOnConflictAsync(() => StartOnceAsync(userId, sessionId, cancellationToken));

    private async Task<GameSessionDto> StartOnceAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);

        var session = access.Session;
        if (session.Status != SessionStatus.Waiting)
        {
            throw AppException.Conflict("session.not_waiting", "A partida já começou ou terminou.");
        }

        var definition = access.Module.Definition;
        var players = await db.SessionPlayers
            .Where(p => p.SessionId == session.Id && p.Status == PlayerStatus.Joined)
            .OrderBy(p => p.Seat)
            .ToListAsync(cancellationToken);

        CheckLineup(definition, players);

        // O jogo precisa saber quem não tem conta (perfil sem celular): o anfitrião age por essa pessoa.
        var memberIds = players.Select(p => p.MemberId).ToList();
        var hasAccount = await db.GroupMembers.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, HasAccount = m.UserId != null })
            .ToDictionaryAsync(x => x.Id, x => x.HasAccount, cancellationToken);

        var setup = new GameSetup(
            session.Id,
            players.Select(p => new SetupPlayer(p.Id, p.TeamNo, p.Seat, hasAccount.GetValueOrDefault(p.MemberId, true))).ToList(),
            GameJson.Parse(session.ConfigJson));

        var transition = Run(() => access.Module.Start(setup, reader.NewContext()));
        var now = time.GetUtcNow();

        var added = Record(session, transition, "session.started", actorPlayerId: null, clientActionId: null, players, now);
        session.Start(transition.State.Data.GetRawText(), transition.State.SchemaVersion, added, now);

        await SaveAsync(cancellationToken);
        return await reader.BuildAsync(access, cancellationToken);
    }

    /// <summary>Aplica uma ação de jogo. Idempotente por <c>clientActionId</c>.</summary>
    public Task<ActionResponse> ApplyAsync(Guid userId, Guid sessionId, GameActionRequest request, CancellationToken cancellationToken) =>
        RetryOnConflictAsync(() => ApplyOnceAsync(userId, sessionId, request, cancellationToken));

    private async Task<ActionResponse> ApplyOnceAsync(Guid userId, Guid sessionId, GameActionRequest request, CancellationToken cancellationToken)
    {
        var clientActionId = request.ClientActionId!.Value;
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);

        // Já aplicada (reenvio depois de uma falha de rede, ou duas abas): devolve o estado atual.
        if (await db.GameEvents.AnyAsync(e => e.SessionId == sessionId && e.ClientActionId == clientActionId, cancellationToken))
        {
            return new ActionResponse(await reader.BuildAsync(access, cancellationToken), Replayed: true);
        }

        var session = access.Session;
        if (session.Status != SessionStatus.InProgress)
        {
            throw AppException.Conflict("session.not_in_progress", "A partida não está em andamento.");
        }

        if (access.MyPlayer is null && !access.CanManage)
        {
            throw AppException.Forbidden("session.not_a_player", "Você não está jogando esta partida.");
        }

        var action = new GameAction(request.Type, request.Payload is { ValueKind: JsonValueKind.Object } payload ? payload.Clone() : GameJson.EmptyObject);
        var transition = Run(() => access.Module.Apply(SessionReader.StateOf(session), action, access.Actor, reader.NewContext()));
        var now = time.GetUtcNow();

        var players = await db.SessionPlayers
            .Where(p => p.SessionId == session.Id && p.Status == PlayerStatus.Joined)
            .ToListAsync(cancellationToken);

        var added = Record(session, transition, $"action.{request.Type}", access.MyPlayer?.Id, clientActionId, players, now);
        session.Advance(transition.State.Data.GetRawText(), transition.State.SchemaVersion, added, now);

        if (transition.IsFinished)
        {
            Conclude(session, access.Module, transition.State, players, access.MyPlayer?.Id, now);
        }

        await SaveAsync(cancellationToken);
        return new ActionResponse(await reader.BuildAsync(access, cancellationToken), Replayed: false);
    }

    /// <summary>Encerra a partida antes do fim natural (o placar do momento vira o resultado).</summary>
    public Task<GameSessionDto> FinishAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        RetryOnConflictAsync(() => FinishOnceAsync(userId, sessionId, cancellationToken));

    private async Task<GameSessionDto> FinishOnceAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);

        var session = access.Session;
        if (session.Status != SessionStatus.InProgress)
        {
            throw AppException.Conflict("session.not_in_progress", "A partida não está em andamento.");
        }

        var players = await db.SessionPlayers
            .Where(p => p.SessionId == session.Id && p.Status == PlayerStatus.Joined)
            .ToListAsync(cancellationToken);

        Conclude(session, access.Module, SessionReader.StateOf(session), players, access.MyPlayer?.Id, time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(access, cancellationToken);
    }

    /// <summary>A trilha da partida depois de <paramref name="after"/> (sequência), em ordem. Só fatos públicos.</summary>
    public async Task<IReadOnlyList<EventDto>> EventsAsync(Guid userId, Guid sessionId, int after, int limit, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        var take = Math.Clamp(limit, 1, options.Value.MaxEventsPerPage);

        var rows = await db.GameEvents.AsNoTracking()
            .Where(e => e.SessionId == access.Session.Id && e.Seq > after)
            .OrderBy(e => e.Seq)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(e => new EventDto(e.Seq, e.Type, e.ActorPlayerId, e.PayloadJson is null ? null : GameJson.Parse(e.PayloadJson), e.CreatedAt))
            .ToList();
    }

    /// <summary>Fim da partida: pede a classificação ao módulo, grava o resultado de cada jogador e encerra.</summary>
    private void Conclude(
        GameSession session,
        IGameModule module,
        GameState state,
        IReadOnlyList<SessionPlayer> players,
        Guid? actorPlayerId,
        DateTimeOffset now)
    {
        var result = Run(() => module.Finish(state));
        var byId = players.ToDictionary(p => p.Id);

        foreach (var standing in result.Standings)
        {
            if (!byId.TryGetValue(standing.PlayerId, out var player))
            {
                throw new InvalidOperationException($"O módulo \"{module.Definition.Id}\" classificou um jogador que não está na partida.");
            }

            db.SessionResults.Add(SessionResult.Create(
                session.Id,
                player.Id,
                player.MemberId,
                session.GroupId,
                session.GameId,
                standing.Team ?? player.TeamNo,
                standing.Rank,
                standing.Score,
                standing.IsWinner,
                now));
        }

        db.GameEvents.Add(GameEventRecord.Create(session.Id, session.LastEventSeq + 1, "session.finished", actorPlayerId, null, null, now));
        session.Finish(eventsAdded: 1, now);
    }

    /// <summary>Confere a escalação: número de jogadores e, em jogos com times, todos alocados e cada time completo.</summary>
    private static void CheckLineup(GameDefinition definition, IReadOnlyList<SessionPlayer> players)
    {
        if (players.Count < definition.MinPlayers)
        {
            throw AppException.Conflict("session.not_enough_players", $"São precisos pelo menos {definition.MinPlayers} jogadores para começar.");
        }

        if (players.Count > definition.MaxPlayers)
        {
            throw AppException.Conflict("session.too_many_players", $"A partida aceita no máximo {definition.MaxPlayers} jogadores.");
        }

        if (definition.TeamCount == 0)
        {
            return;
        }

        if (players.Any(p => p.TeamNo is null))
        {
            throw AppException.Conflict("session.teams_incomplete", "Todos os jogadores precisam estar em um time para começar.");
        }

        for (var team = 0; team < definition.TeamCount; team++)
        {
            if (players.Count(p => p.TeamNo == team) < definition.MinPlayersPerTeam)
            {
                throw AppException.Conflict(
                    "session.teams_incomplete",
                    $"Cada time precisa de pelo menos {definition.MinPlayersPerTeam} jogador(es); o time {team + 1} está incompleto.");
            }
        }
    }

    /// <summary>
    /// Registra o que a transição produziu: um evento da própria ação (que carrega o <c>clientActionId</c>), os eventos do módulo
    /// e o livro-razão de pontos. Devolve quantos eventos foram criados (para a partida avançar o contador de sequência).
    /// </summary>
    private int Record(
        GameSession session,
        GameTransition transition,
        string headType,
        Guid? actorPlayerId,
        Guid? clientActionId,
        IReadOnlyList<SessionPlayer> players,
        DateTimeOffset now)
    {
        var seq = session.LastEventSeq;
        var firstSeq = seq + 1;

        db.GameEvents.Add(GameEventRecord.Create(session.Id, ++seq, headType, actorPlayerId, clientActionId, null, now));

        foreach (var gameEvent in transition.Events)
        {
            db.GameEvents.Add(GameEventRecord.Create(
                session.Id,
                ++seq,
                gameEvent.Type,
                gameEvent.ActorPlayerId ?? actorPlayerId,
                null,
                gameEvent.Payload?.GetRawText(),
                now));
        }

        var known = players.Select(p => p.Id).ToHashSet();
        foreach (var change in transition.Points)
        {
            if (change.PlayerId is { } playerId && !known.Contains(playerId))
            {
                throw new InvalidOperationException("O módulo pontuou um jogador que não está na partida.");
            }

            db.ScoreEntries.Add(ScoreEntry.Create(session.Id, change.PlayerId, change.Team, change.Points, change.Reason, firstSeq, now));
        }

        return seq - session.LastEventSeq;
    }

    /// <summary>Executa o módulo traduzindo <see cref="RuleViolation"/> para o erro HTTP correspondente.</summary>
    private static T Run<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (RuleViolation violation)
        {
            var kind = violation.Kind switch
            {
                RuleViolationKind.InvalidAction => ErrorKind.Validation,
                RuleViolationKind.NotAllowed => ErrorKind.Forbidden,
                _ => ErrorKind.Conflict,
            };

            throw new AppException(kind, violation.Code, violation.Message);
        }
    }

    /// <summary>Grava; uma corrida (versão mudou, ou o mesmo clientActionId entrou em paralelo) vira <see cref="SaveConflictException"/>.</summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SaveConflictException();
        }
        catch (DbUpdateException exception) when (dbErrors.IsUniqueViolation(exception))
        {
            throw new SaveConflictException();
        }
    }

    /// <summary>
    /// Perdeu uma corrida: descarta o estado rastreado e refaz a operação inteira sobre o estado novo (relê a partida, revalida,
    /// reaplica). Se for um reenvio de <c>clientActionId</c>, a nova leitura o detecta e responde "replayed". Persistindo, 409.
    /// </summary>
    private async Task<T> RetryOnConflictAsync<T>(Func<Task<T>> operation)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SaveConflictException)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw SessionReader.ConcurrentUpdate();
    }

    private sealed class SaveConflictException : Exception;
}
