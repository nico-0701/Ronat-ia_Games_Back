using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Games;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Sessions;

/// <summary>Quem consulta uma partida: a sessão (rastreada), o membro do grupo, o jogador (se joga) e o que pode gerenciar.</summary>
public sealed record SessionAccess(GameSession Session, GroupMember Me, SessionPlayer? MyPlayer, bool CanManage, IGameModule Module)
{
    public GameActor Actor => new(MyPlayer?.Id, MyPlayer?.TeamNo, CanManage);
}

/// <summary>
/// Acesso e leitura das partidas. Quem não é membro do grupo recebe sempre <c>session.not_found</c> (o servidor não revela que a
/// partida existe). A visão do jogo é montada <b>por quem consulta</b>: o módulo só devolve o que essa pessoa pode ver.
/// </summary>
public sealed class SessionReader(
    IAppDbContext db,
    GroupAccess groups,
    IGameCatalog catalog,
    IGameRandom random,
    TimeProvider time)
{
    public static AppException NotFound() => AppException.NotFound("session.not_found", "Partida não encontrada.");

    public static AppException Forbidden() =>
        AppException.Forbidden("session.forbidden", "Só o anfitrião da partida ou um administrador do grupo pode fazer isso.");

    public static AppException ConcurrentUpdate() => AppException.Conflict(
        "session.concurrent_update",
        "A partida mudou ao mesmo tempo por outra ação. Atualize e tente de novo.");

    public IGameContext NewContext() => new GameContext(time.GetUtcNow(), random);

    /// <summary>Carrega a partida para quem é membro ativo do grupo dela (rastreada, para alteração).</summary>
    public async Task<SessionAccess> RequireAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.GameSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw NotFound();

        GroupMember me;
        try
        {
            (_, me) = await groups.RequireMemberAsync(userId, session.GroupId, cancellationToken);
        }
        catch (AppException exception) when (exception.Code == "group.not_found")
        {
            throw NotFound();
        }

        var player = await db.SessionPlayers.SingleOrDefaultAsync(
            p => p.SessionId == session.Id && p.MemberId == me.Id && p.Status == PlayerStatus.Joined,
            cancellationToken);

        var canManage = session.HostMemberId == me.Id || me.Role >= GroupRole.Admin;
        return new SessionAccess(session, me, player, canManage, catalog.Require(session.GameId));
    }

    public static void RequireManage(SessionAccess access)
    {
        if (!access.CanManage)
        {
            throw Forbidden();
        }
    }

    public static GameState StateOf(GameSession session) =>
        new(session.StateSchemaVersion ?? 1, GameJson.Parse(session.StateJson ?? "{}"));

    /// <summary>A partida como <paramref name="access"/> a enxerga: lobby, placares e a visão do jogo para esta pessoa.</summary>
    public async Task<GameSessionDto> BuildAsync(SessionAccess access, CancellationToken cancellationToken)
    {
        var session = access.Session;
        var viewerUserId = access.Me.UserId;

        var rows = await (
            from player in db.SessionPlayers.AsNoTracking()
            where player.SessionId == session.Id && player.Status == PlayerStatus.Joined
            join member in db.GroupMembers.AsNoTracking() on player.MemberId equals member.Id
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            select new { player, member, user }).ToListAsync(cancellationToken);

        var playerScores = await db.ScoreEntries.AsNoTracking()
            .Where(e => e.SessionId == session.Id && e.PlayerId != null)
            .GroupBy(e => e.PlayerId)
            .Select(g => new { PlayerId = g.Key, Points = g.Sum(e => e.Points) })
            .ToListAsync(cancellationToken);

        var teamScores = await db.ScoreEntries.AsNoTracking()
            .Where(e => e.SessionId == session.Id && e.TeamNo != null)
            .GroupBy(e => e.TeamNo)
            .Select(g => new { Team = g.Key, Points = g.Sum(e => e.Points) })
            .ToListAsync(cancellationToken);

        var scoreByPlayer = playerScores.ToDictionary(x => x.PlayerId!.Value, x => x.Points);

        var players = rows
            .OrderBy(row => row.player.Seat)
            .Select(row =>
            {
                var member = GroupAccess.ToDto(row.member, row.user, viewerUserId);
                return new SessionPlayerDto(
                    row.player.Id,
                    row.member.Id,
                    member.DisplayName,
                    member.Avatar,
                    member.HasAccount,
                    row.player.TeamNo,
                    row.player.Seat,
                    scoreByPlayer.GetValueOrDefault(row.player.Id),
                    member.IsMe);
            })
            .ToList();

        // Todos os times do jogo aparecem no placar, mesmo os que ainda não pontuaram.
        var teamCount = access.Module.Definition.TeamCount;
        var teams = Enumerable.Range(0, teamCount)
            .Select(team => new TeamScoreDto(team, teamScores.FirstOrDefault(x => x.Team == team)?.Points ?? 0))
            .ToList();

        JsonElement? view = null;
        IReadOnlyList<string> allowed = [];
        DateTimeOffset? deadline = null;

        if (session.StateJson is not null)
        {
            var projection = access.Module.Project(StateOf(session), access.Actor, NewContext());
            view = projection.View;
            allowed = session.Status == SessionStatus.InProgress ? projection.AllowedActions : [];
            deadline = session.Status == SessionStatus.InProgress ? projection.DeadlineAt : null;
        }

        var standings = session.Status == SessionStatus.Finished
            ? await StandingsAsync(session.Id, players, cancellationToken)
            : [];

        return new GameSessionDto(
            session.Id,
            session.GroupId,
            session.GameId,
            session.RulesVersion,
            session.Status,
            session.Version,
            session.HostMemberId,
            access.CanManage,
            access.Me.Id,
            access.MyPlayer?.Id,
            GameJson.Parse(session.ConfigJson),
            players,
            teams,
            view,
            allowed,
            deadline,
            standings,
            session.CreatedAt,
            session.StartedAt,
            session.FinishedAt);
    }

    private async Task<IReadOnlyList<StandingDto>> StandingsAsync(Guid sessionId, IReadOnlyList<SessionPlayerDto> players, CancellationToken cancellationToken)
    {
        var results = await db.SessionResults.AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .OrderBy(r => r.Rank)
            .ToListAsync(cancellationToken);

        var names = players.ToDictionary(p => p.Id, p => p.DisplayName);
        return results
            .Select(r => new StandingDto(r.PlayerId, r.MemberId, names.GetValueOrDefault(r.PlayerId, string.Empty), r.TeamNo, r.Rank, r.Score, r.IsWinner))
            .ToList();
    }
}
