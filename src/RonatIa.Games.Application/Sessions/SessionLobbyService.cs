using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RonatIa.Games.Abstractions;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Games;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Sessions;

/// <summary>
/// O lobby de uma partida: criar, quem entra e sai, times, configuração, cancelar e revanche. Quem gerencia é o anfitrião
/// (quem criou) ou um administrador do grupo; qualquer membro do grupo com conta entra por conta própria.
/// Começar e jogar ficam em <see cref="SessionPlayService"/>.
/// </summary>
public sealed class SessionLobbyService(
    IAppDbContext db,
    SessionReader reader,
    GroupAccess groups,
    IGameCatalog catalog,
    IGameRandom random,
    IDbExceptionClassifier dbErrors,
    IOptions<SessionOptions> options,
    TimeProvider time)
{
    public IReadOnlyList<GameDto> Games() => catalog.All.Select(module => ToDto(module.Definition)).ToList();

    public GameDto Game(string gameId) => ToDto(catalog.Require(gameId).Definition);

    public async Task<GameSessionDto> CreateAsync(Guid userId, CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var module = catalog.Require(request.GameId);
        var (group, me) = await groups.RequireMemberAsync(userId, request.GroupId!.Value, cancellationToken);
        var config = ValidateConfig(module, request.Config);
        await EnsureRoomForSessionAsync(group.Id, cancellationToken);

        var now = time.GetUtcNow();
        var session = GameSession.Create(group.Id, module.Definition.Id, module.Definition.RulesVersion, me.Id, config, now);

        db.GameSessions.Add(session);
        db.SessionPlayers.Add(SessionPlayer.Join(session.Id, me.Id, seat: 0, now));
        await db.SaveChangesAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, session.Id, cancellationToken), cancellationToken);
    }

    /// <summary>As partidas do grupo: as ativas primeiro, depois as mais recentes.</summary>
    public async Task<IReadOnlyList<GameSessionSummaryDto>> ListAsync(Guid userId, Guid groupId, int limit, CancellationToken cancellationToken)
    {
        await groups.RequireMemberAsync(userId, groupId, cancellationToken);

        var rows = await db.GameSessions.AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.Status == SessionStatus.Waiting || s.Status == SessionStatus.InProgress)
            .ThenByDescending(s => s.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(s => new
            {
                s.Id,
                s.GameId,
                s.Status,
                s.HostMemberId,
                s.CreatedAt,
                s.StartedAt,
                s.FinishedAt,
                PlayerCount = db.SessionPlayers.Count(p => p.SessionId == s.Id && p.Status == PlayerStatus.Joined),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new GameSessionSummaryDto(r.Id, r.GameId, r.Status, r.HostMemberId, r.PlayerCount, r.CreatedAt, r.StartedAt, r.FinishedAt))
            .ToList();
    }

    public async Task<GameSessionDto> GetAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);

    /// <summary>Entra no lobby como a própria pessoa. Quem já está recebe a partida como está (idempotente).</summary>
    public async Task<GameSessionDto> JoinAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        RequireWaiting(access.Session);

        if (access.MyPlayer is not null)
        {
            return await reader.BuildAsync(access, cancellationToken);
        }

        await AddMemberAsync(access, access.Me.Id, cancellationToken);
        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    public async Task<GameSessionDto> LeaveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        RequireWaiting(access.Session);

        if (access.MyPlayer is not { } player)
        {
            throw AppException.NotFound("session.player_not_found", "Você não está nesta partida.");
        }

        player.Leave(time.GetUtcNow());
        access.Session.LobbyChanged(time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    /// <summary>O anfitrião adiciona um membro do grupo (inclusive um perfil sem conta, que não tem celular).</summary>
    public async Task<GameSessionDto> AddPlayerAsync(Guid userId, Guid sessionId, AddSessionPlayerRequest request, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);
        RequireWaiting(access.Session);

        var memberId = request.MemberId!.Value;
        var isGroupMember = await db.GroupMembers.AnyAsync(
            m => m.Id == memberId && m.GroupId == access.Session.GroupId && m.Status == MemberStatus.Active,
            cancellationToken);

        if (!isGroupMember)
        {
            throw GroupAccess.MemberNotFound();
        }

        await AddMemberAsync(access, memberId, cancellationToken);
        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    public async Task<GameSessionDto> RemovePlayerAsync(Guid userId, Guid sessionId, Guid playerId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);
        RequireWaiting(access.Session);

        var player = await FindActivePlayerAsync(access.Session.Id, playerId, cancellationToken);
        var now = time.GetUtcNow();
        player.Remove(now);
        access.Session.LobbyChanged(now);
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    /// <summary>Define o time de cada jogador informado (os demais ficam como estão). Só em jogos com times.</summary>
    public async Task<GameSessionDto> AssignTeamsAsync(Guid userId, Guid sessionId, AssignTeamsRequest request, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);
        RequireWaiting(access.Session);

        var teamCount = RequireTeams(access.Module);
        var ids = request.Assignments.Select(a => a.PlayerId!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count)
        {
            throw InvalidTeam("Cada jogador aparece uma única vez.");
        }

        if (request.Assignments.Any(a => a.Team is { } team && (team < 0 || team >= teamCount)))
        {
            throw InvalidTeam($"O time vai de 0 a {teamCount - 1}.");
        }

        var players = await db.SessionPlayers
            .Where(p => p.SessionId == access.Session.Id && p.Status == PlayerStatus.Joined && ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (players.Count != ids.Count)
        {
            throw AppException.NotFound("session.player_not_found", "Algum jogador não está nesta partida.");
        }

        foreach (var assignment in request.Assignments)
        {
            players.Single(p => p.Id == assignment.PlayerId).AssignTeam(assignment.Team);
        }

        access.Session.LobbyChanged(time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    /// <summary>Sorteia os times: embaralha os jogadores e distribui em rodízio, para os times ficarem equilibrados.</summary>
    public async Task<GameSessionDto> ShuffleTeamsAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);
        RequireWaiting(access.Session);

        var teamCount = RequireTeams(access.Module);
        var players = await db.SessionPlayers
            .Where(p => p.SessionId == access.Session.Id && p.Status == PlayerStatus.Joined)
            .ToListAsync(cancellationToken);

        random.Shuffle(players);
        for (var i = 0; i < players.Count; i++)
        {
            players[i].AssignTeam(i % teamCount);
        }

        access.Session.LobbyChanged(time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, sessionId, cancellationToken), cancellationToken);
    }

    public async Task<GameSessionDto> UpdateConfigAsync(Guid userId, Guid sessionId, UpdateSessionConfigRequest request, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);

        access.Session.UpdateConfig(ValidateConfig(access.Module, request.Config), time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(access, cancellationToken);
    }

    public async Task<GameSessionDto> CancelAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);

        access.Session.Cancel(time.GetUtcNow());
        await SaveAsync(cancellationToken);

        return await reader.BuildAsync(access, cancellationToken);
    }

    /// <summary>Uma partida nova no lobby, com o mesmo jogo, a mesma configuração e os mesmos jogadores (e times) da que terminou.</summary>
    public async Task<GameSessionDto> RematchAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var access = await reader.RequireAsync(userId, sessionId, cancellationToken);
        SessionReader.RequireManage(access);

        var previous = access.Session;
        if (previous.IsActive)
        {
            throw AppException.Conflict("session.not_ended", "A revanche só vale depois que a partida termina ou é cancelada.");
        }

        var config = ValidateConfig(access.Module, GameJson.Parse(previous.ConfigJson));
        await EnsureRoomForSessionAsync(previous.GroupId, cancellationToken);

        var now = time.GetUtcNow();
        var session = GameSession.Create(
            previous.GroupId,
            previous.GameId,
            access.Module.Definition.RulesVersion,
            access.Me.Id,
            config,
            now,
            rematchOfId: previous.Id);

        // Só volta quem ainda é membro ativo do grupo.
        var carried = await (
            from player in db.SessionPlayers.AsNoTracking()
            where player.SessionId == previous.Id && player.Status == PlayerStatus.Joined
            join member in db.GroupMembers.AsNoTracking() on player.MemberId equals member.Id
            where member.Status == MemberStatus.Active
            orderby player.Seat
            select new { player.MemberId, player.TeamNo }).ToListAsync(cancellationToken);

        db.GameSessions.Add(session);
        var seat = 0;
        foreach (var item in carried)
        {
            var player = SessionPlayer.Join(session.Id, item.MemberId, seat++, now);
            player.AssignTeam(item.TeamNo);
            db.SessionPlayers.Add(player);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await reader.BuildAsync(await reader.RequireAsync(userId, session.Id, cancellationToken), cancellationToken);
    }

    private async Task AddMemberAsync(SessionAccess access, Guid memberId, CancellationToken cancellationToken)
    {
        var session = access.Session;
        var definition = access.Module.Definition;
        var now = time.GetUtcNow();

        var existing = await db.SessionPlayers.SingleOrDefaultAsync(
            p => p.SessionId == session.Id && p.MemberId == memberId,
            cancellationToken);

        if (existing is { IsActive: true })
        {
            return; // já está (idempotente)
        }

        var active = await db.SessionPlayers.CountAsync(p => p.SessionId == session.Id && p.Status == PlayerStatus.Joined, cancellationToken);
        if (active >= definition.MaxPlayers)
        {
            throw AppException.Conflict("session.full", $"A partida já tem o máximo de {definition.MaxPlayers} jogadores.");
        }

        var seat = (await db.SessionPlayers.Where(p => p.SessionId == session.Id).MaxAsync(p => (int?)p.Seat, cancellationToken) ?? -1) + 1;

        if (existing is not null)
        {
            existing.Rejoin(seat, now);
        }
        else
        {
            db.SessionPlayers.Add(SessionPlayer.Join(session.Id, memberId, seat, now));
        }

        session.LobbyChanged(now);
        await SaveAsync(cancellationToken);
    }

    private async Task<SessionPlayer> FindActivePlayerAsync(Guid sessionId, Guid playerId, CancellationToken cancellationToken) =>
        await db.SessionPlayers.SingleOrDefaultAsync(
            p => p.Id == playerId && p.SessionId == sessionId && p.Status == PlayerStatus.Joined,
            cancellationToken)
        ?? throw AppException.NotFound("session.player_not_found", "Jogador não encontrado nesta partida.");

    private async Task EnsureRoomForSessionAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var active = await db.GameSessions.CountAsync(
            s => s.GroupId == groupId && (s.Status == SessionStatus.Waiting || s.Status == SessionStatus.InProgress),
            cancellationToken);

        if (active >= options.Value.MaxActiveSessionsPerGroup)
        {
            throw AppException.Conflict(
                "session.limit_reached",
                $"O grupo já tem {active} partidas abertas, que é o máximo. Termine ou cancele alguma antes de criar outra.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw SessionReader.ConcurrentUpdate();
        }
        catch (DbUpdateException exception) when (dbErrors.IsUniqueViolation(exception))
        {
            // Duas chamadas iguais ao mesmo tempo (ex.: duplo clique em "entrar"); repetir é seguro.
            throw SessionReader.ConcurrentUpdate();
        }
    }

    private static void RequireWaiting(GameSession session)
    {
        if (session.Status != SessionStatus.Waiting)
        {
            throw AppException.Conflict("session.not_waiting", "Isso só pode ser feito enquanto a partida está no lobby.");
        }
    }

    private static int RequireTeams(IGameModule module) => module.Definition.TeamCount > 0
        ? module.Definition.TeamCount
        : throw AppException.Conflict("session.no_teams", "Este jogo não tem times.");

    private static AppException InvalidTeam(string message) =>
        AppException.Validation("session.invalid_team", message, new Dictionary<string, string[]> { ["assignments"] = [message] });

    /// <summary>Valida a configuração com o módulo e devolve o JSON normalizado, ou <c>session.invalid_config</c> com os erros por campo.</summary>
    internal static string ValidateConfig(IGameModule module, JsonElement? config)
    {
        var result = module.ValidateConfig(config);
        if (!result.IsValid)
        {
            throw AppException.Validation("session.invalid_config", "A configuração da partida é inválida.", result.Errors);
        }

        return result.Normalized!.Value.GetRawText();
    }

    private static GameDto ToDto(GameDefinition d) => new(
        d.Id,
        d.Name,
        d.Description,
        d.RulesVersion,
        d.MinPlayers,
        d.MaxPlayers,
        d.TeamCount,
        d.MinPlayersPerTeam,
        d.ConfigDefaults);
}
