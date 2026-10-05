using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Application.Users;

/// <summary>Direitos da pessoa sobre os próprios dados (LGPD): acesso e portabilidade. A exclusão está em <see cref="UserService"/>.</summary>
public sealed class PrivacyService(IAppDbContext db, TimeProvider time)
{
    public async Task<PersonalDataExportDto> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.NotFound("user.not_found", "Conta não encontrada.");

        var logins = await db.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new ExportedLoginDto(s.Id, s.DeviceLabel, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.RevokedAt, s.RevokedReason))
            .ToListAsync(cancellationToken);

        var memberships = await (
            from member in db.GroupMembers.AsNoTracking()
            where member.UserId == userId
            join g in db.Groups.AsNoTracking() on member.GroupId equals g.Id
            orderby member.JoinedAt
            select new ExportedMembershipDto(member.Id, g.Id, g.Name, member.Role, member.Status, member.JoinedAt, member.LeftAt))
            .ToListAsync(cancellationToken);

        var memberIds = db.GroupMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.Id);
        var results = await db.SessionResults.AsNoTracking()
            .Where(r => memberIds.Contains(r.MemberId))
            .OrderBy(r => r.FinishedAt)
            .Select(r => new ExportedResultDto(r.SessionId, r.GroupId, r.GameId, r.FinishedAt, r.TeamNo, r.Rank, r.Score, r.IsWinner))
            .ToListAsync(cancellationToken);

        return new PersonalDataExportDto(
            time.GetUtcNow(),
            new ExportedProfileDto(
                user.Id,
                user.DisplayName,
                UserMapper.ToAvatarDto(user.AvatarPhotoId, user.AvatarPreset),
                user.PhoneLast4,
                user.CreatedAt,
                user.LastLoginAt,
                user.TermsAcceptedAt,
                user.TermsVersion),
            logins,
            memberships,
            results);
    }
}
