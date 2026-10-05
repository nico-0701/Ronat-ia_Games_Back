using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Application.Ranking;

/// <summary>
/// Ranking e histórico de um grupo, calculados dos resultados das partidas encerradas (<c>session_results</c>). O histórico
/// pertence ao <b>membro</b> do grupo: um perfil sem conta que alguém assume depois leva consigo tudo o que já jogou.
/// Só membros ativos do grupo consultam (senão, 404).
/// </summary>
public sealed class RankingService(IAppDbContext db, GroupAccess groups, TimeProvider time)
{
    private const int MaxPageSize = 50;

    public async Task<RankingDto> RankingAsync(Guid userId, Guid groupId, string? gameId, RankingPeriod period, CancellationToken cancellationToken)
    {
        await groups.RequireMemberAsync(userId, groupId, cancellationToken);
        var since = SinceOf(period);

        var results = db.SessionResults.AsNoTracking().Where(r => r.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            results = results.Where(r => r.GameId == gameId);
        }

        if (since is { } from)
        {
            results = results.Where(r => r.FinishedAt >= from);
        }

        var totals = await results
            .GroupBy(r => r.MemberId)
            .Select(g => new
            {
                MemberId = g.Key,
                Played = g.Count(),
                Wins = g.Count(r => r.IsWinner),
                Score = g.Sum(r => r.Score),
            })
            .ToListAsync(cancellationToken);

        var people = await PeopleAsync(totals.Select(t => t.MemberId), userId, cancellationToken);

        var ordered = totals
            .Select(t => new { Total = t, Person = people[t.MemberId], WinRate = t.Played == 0 ? 0 : (double)t.Wins / t.Played })
            .OrderByDescending(x => x.Total.Wins)
            .ThenByDescending(x => x.WinRate)
            .ThenByDescending(x => x.Total.Played)
            .ThenBy(x => x.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Total.MemberId)
            .ToList();

        // Posição de competição: quem empata em tudo divide a posição (1, 1, 3...).
        var entries = ordered
            .Select(x => new RankingEntryDto(
                Rank: 1 + ordered.Count(other => other.Total.Wins > x.Total.Wins
                    || (other.Total.Wins == x.Total.Wins && other.WinRate > x.WinRate)
                    || (other.Total.Wins == x.Total.Wins && other.WinRate == x.WinRate && other.Total.Played > x.Total.Played)),
                x.Total.MemberId,
                x.Person.DisplayName,
                x.Person.Avatar,
                x.Person.HasAccount,
                x.Person.IsMe,
                x.Total.Played,
                x.Total.Wins,
                Math.Round(x.WinRate, 4),
                x.Total.Score))
            .ToList();

        return new RankingDto(string.IsNullOrWhiteSpace(gameId) ? null : gameId, period, since, entries);
    }

    /// <summary>As partidas encerradas do grupo, da mais recente para a mais antiga, com a classificação de cada uma.</summary>
    public async Task<HistoryPageDto> HistoryAsync(Guid userId, Guid groupId, string? gameId, DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        await groups.RequireMemberAsync(userId, groupId, cancellationToken);
        var take = Math.Clamp(limit, 1, MaxPageSize);

        var sessions = db.GameSessions.AsNoTracking()
            .Where(s => s.GroupId == groupId && s.Status == SessionStatus.Finished && s.FinishedAt != null);

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            sessions = sessions.Where(s => s.GameId == gameId);
        }

        if (before is { } cursor)
        {
            sessions = sessions.Where(s => s.FinishedAt < cursor);
        }

        var page = await sessions
            .OrderByDescending(s => s.FinishedAt)
            .ThenByDescending(s => s.Id)
            .Take(take + 1)
            .Select(s => new { s.Id, s.GameId, s.StartedAt, FinishedAt = s.FinishedAt!.Value })
            .ToListAsync(cancellationToken);

        var more = page.Count > take;
        page = [.. page.Take(take)];

        var ids = page.Select(s => s.Id).ToList();
        var results = await db.SessionResults.AsNoTracking()
            .Where(r => ids.Contains(r.SessionId))
            .ToListAsync(cancellationToken);

        var people = await PeopleAsync(results.Select(r => r.MemberId).Distinct(), userId, cancellationToken);

        var items = page
            .Select(s => new HistoryEntryDto(
                s.Id,
                s.GameId,
                s.StartedAt,
                s.FinishedAt,
                results
                    .Where(r => r.SessionId == s.Id)
                    .OrderBy(r => r.Rank)
                    .ThenBy(r => r.TeamNo)
                    .ThenBy(r => people[r.MemberId].DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(r => new HistoryStandingDto(r.MemberId, people[r.MemberId].DisplayName, people[r.MemberId].Avatar, r.TeamNo, r.Rank, r.Score, r.IsWinner))
                    .ToList()))
            .ToList();

        return new HistoryPageDto(items, more ? page[^1].FinishedAt : null);
    }

    /// <summary>As estatísticas da própria pessoa, somando os grupos de que participa (e os perfis que já assumiu).</summary>
    public async Task<MyStatsDto> MyStatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberIds = db.GroupMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.Id);

        var byGame = await db.SessionResults.AsNoTracking()
            .Where(r => memberIds.Contains(r.MemberId))
            .GroupBy(r => r.GameId)
            .Select(g => new { GameId = g.Key, Played = g.Count(), Wins = g.Count(r => r.IsWinner), Score = g.Sum(r => r.Score) })
            .ToListAsync(cancellationToken);

        var groupCount = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            .Join(db.Groups.Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (member, _) => member.Id)
            .CountAsync(cancellationToken);

        return new MyStatsDto(
            byGame.Sum(g => g.Played),
            byGame.Sum(g => g.Wins),
            groupCount,
            byGame.OrderBy(g => g.GameId, StringComparer.Ordinal).Select(g => new GameStatsDto(g.GameId, g.Played, g.Wins, g.Score)).ToList());
    }

    private DateTimeOffset? SinceOf(RankingPeriod period) => period switch
    {
        RankingPeriod.Year => time.GetUtcNow().AddDays(-365),
        RankingPeriod.Quarter => time.GetUtcNow().AddDays(-90),
        RankingPeriod.Month => time.GetUtcNow().AddDays(-30),
        RankingPeriod.Week => time.GetUtcNow().AddDays(-7),
        _ => null,
    };

    /// <summary>Nome e avatar de cada membro (quem tem conta usa os da conta), inclusive de quem já saiu do grupo.</summary>
    private async Task<Dictionary<Guid, MemberPerson>> PeopleAsync(IEnumerable<Guid> memberIds, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var ids = memberIds.Distinct().ToList();
        var rows = await (
            from member in db.GroupMembers.AsNoTracking()
            where ids.Contains(member.Id)
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            select new { member, user }).ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.member.Id,
            row =>
            {
                var dto = GroupAccess.ToDto(row.member, row.user, viewerUserId);
                return new MemberPerson(dto.DisplayName, dto.Avatar, dto.HasAccount, dto.IsMe);
            });
    }

    private sealed record MemberPerson(string DisplayName, AvatarDto Avatar, bool HasAccount, bool IsMe);
}
