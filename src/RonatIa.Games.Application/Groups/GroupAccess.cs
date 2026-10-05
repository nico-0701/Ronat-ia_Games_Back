using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Application.Users;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Groups;

/// <summary>
/// Consultas e regras comuns dos casos de uso de grupos: quem é membro, montagem dos DTOs, limites e gravação.
/// Quem não é membro (ou o grupo foi excluído) recebe sempre o mesmo <c>group.not_found</c>: o servidor não revela que o grupo existe.
/// </summary>
public sealed class GroupAccess(IAppDbContext db, IOptions<GroupOptions> options)
{
    public static AppException NotFound() => AppException.NotFound("group.not_found", "Grupo não encontrado.");

    public static AppException MemberNotFound() => AppException.NotFound("member.not_found", "Membro não encontrado neste grupo.");

    public static AppException Forbidden() =>
        AppException.Forbidden("group.forbidden", "Você não tem permissão para fazer isso neste grupo.");

    public static void Require(bool allowed)
    {
        if (!allowed)
        {
            throw Forbidden();
        }
    }

    /// <summary>Carrega o grupo e o vínculo ativo da pessoa (rastreados, para alteração).</summary>
    public async Task<(Group Group, GroupMember Me)> RequireMemberAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var row = await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.UserId == userId && m.Status == MemberStatus.Active)
            .Join(db.Groups.Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (member, group) => new { group, member })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? throw NotFound() : (row.group, row.member);
    }

    /// <summary>Um membro ativo do grupo (rastreado), ou <c>member.not_found</c>.</summary>
    public async Task<GroupMember> RequireActiveMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken) =>
        await db.GroupMembers.SingleOrDefaultAsync(
            m => m.Id == memberId && m.GroupId == groupId && m.Status == MemberStatus.Active,
            cancellationToken)
        ?? throw MemberNotFound();

    public async Task<GroupDetailDto> DetailAsync(Group group, GroupMember me, CancellationToken cancellationToken)
    {
        var members = await MembersAsync(group.Id, me.UserId, cancellationToken);
        return new GroupDetailDto(
            group.Id,
            group.Name,
            me.Role,
            me.Id,
            group.InviteEnabled ? group.InviteCode : null,
            group.InviteEnabled,
            members,
            group.CreatedAt);
    }

    /// <summary>Membros ativos: dono, administradores, depois os demais, cada faixa por ordem de entrada.</summary>
    public async Task<IReadOnlyList<MemberDto>> MembersAsync(Guid groupId, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var rows = await (
            from member in db.GroupMembers.AsNoTracking()
            where member.GroupId == groupId && member.Status == MemberStatus.Active
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            select new { member, user }).ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.member.Role)
            .ThenBy(row => row.member.JoinedAt)
            .ThenBy(row => row.member.Id)
            .Select(row => ToDto(row.member, row.user, viewerUserId))
            .ToList();
    }

    public async Task<MemberDto> MemberAsync(GroupMember member, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var user = member.UserId is { } id
            ? await db.Users.AsNoTracking().SingleAsync(u => u.Id == id, cancellationToken)
            : null;

        return ToDto(member, user, viewerUserId);
    }

    public static MemberDto ToDto(GroupMember member, User? user, Guid? viewerUserId) => user is not null
        ? new MemberDto(
            member.Id,
            user.DisplayName,
            UserMapper.ToAvatarDto(user.AvatarPhotoId, user.AvatarPreset),
            member.Role,
            HasAccount: true,
            IsMe: viewerUserId == user.Id,
            member.JoinedAt)
        : new MemberDto(
            member.Id,
            member.DisplayName ?? string.Empty,
            UserMapper.ToAvatarDto(member.AvatarPhotoId, member.AvatarPreset),
            member.Role,
            HasAccount: false,
            IsMe: false,
            member.JoinedAt);

    /// <summary>Quantos grupos (não excluídos) a pessoa tem como dona ou membro.</summary>
    public async Task EnsureBelowGroupLimitAsync(Guid userId, CancellationToken cancellationToken)
    {
        var count = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            .Join(db.Groups.Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (member, _) => member.Id)
            .CountAsync(cancellationToken);

        if (count >= options.Value.MaxGroupsPerUser)
        {
            throw AppException.Conflict(
                "group.limit_reached",
                $"Você já participa de {count} grupos, que é o máximo. Saia de algum para criar ou entrar em outro.");
        }
    }

    /// <summary>O grupo ainda tem vaga (contando perfis sem conta)?</summary>
    public async Task EnsureRoomAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var count = await db.GroupMembers.CountAsync(m => m.GroupId == groupId && m.Status == MemberStatus.Active, cancellationToken);
        if (count >= options.Value.MaxMembersPerGroup)
        {
            throw AppException.Conflict("group.full", $"O grupo já tem o máximo de {options.Value.MaxMembersPerGroup} membros.");
        }
    }

    /// <summary>Grava, traduzindo a falha de concorrência (duas pessoas alterando o mesmo membro) em um erro claro.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrentUpdate();
        }
    }

    public static AppException ConcurrentUpdate() => AppException.Conflict(
        "group.concurrent_update",
        "O grupo foi alterado por outra pessoa ao mesmo tempo. Atualize e tente de novo.");
}
