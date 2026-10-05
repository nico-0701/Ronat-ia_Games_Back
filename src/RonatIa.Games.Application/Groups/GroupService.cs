using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Users;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Application.Groups;

/// <summary>
/// Grupos de amigos: criar, listar, entrar com a senha do grupo (inclusive assumindo um perfil sem conta), sair,
/// gerenciar a senha e excluir. Gerenciar os membros fica em <see cref="GroupMemberService"/>.
/// </summary>
public sealed class GroupService(
    IAppDbContext db,
    GroupAccess access,
    AvatarService avatars,
    IDbExceptionClassifier dbErrors,
    TimeProvider time)
{
    public const string DeleteConfirmation = "EXCLUIR";

    public async Task<GroupDetailDto> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var name = Group.NormalizeName(request.Name);
        await access.EnsureBelowGroupLimitAsync(userId, cancellationToken);

        var now = time.GetUtcNow();
        var group = Group.Create(name, await NewInviteCodeAsync(cancellationToken), now);
        var owner = GroupMember.CreateOwner(group.Id, userId, now);

        db.Groups.Add(group);
        db.GroupMembers.Add(owner);
        await db.SaveChangesAsync(cancellationToken);

        return await access.DetailAsync(group, owner, cancellationToken);
    }

    public async Task<IReadOnlyList<GroupSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
            from member in db.GroupMembers.AsNoTracking()
            join g in db.Groups.AsNoTracking() on member.GroupId equals g.Id
            where member.UserId == userId && member.Status == MemberStatus.Active && g.DeletedAt == null
            select new
            {
                g.Id,
                g.Name,
                member.Role,
                g.CreatedAt,
                MemberCount = db.GroupMembers.Count(other => other.GroupId == g.Id && other.Status == MemberStatus.Active),
            }).ToListAsync(cancellationToken);

        return rows
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Id)
            .Select(row => new GroupSummaryDto(row.Id, row.Name, row.Role, row.MemberCount, row.CreatedAt))
            .ToList();
    }

    public async Task<GroupDetailDto> GetAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        return await access.DetailAsync(group, me, cancellationToken);
    }

    public async Task<GroupDetailDto> UpdateAsync(Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        var now = time.GetUtcNow();

        if (request.Name is not null)
        {
            GroupAccess.Require(GroupPermissions.CanEditGroup(me.Role));
        }

        if (request.InviteEnabled is not null)
        {
            GroupAccess.Require(GroupPermissions.CanManageInvite(me.Role));
        }

        if (request.Name is not null)
        {
            group.Rename(request.Name, now);
        }

        if (request.InviteEnabled is { } enabled)
        {
            group.SetInviteEnabled(enabled, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await access.DetailAsync(group, me, cancellationToken);
    }

    /// <summary>Gera uma senha nova (e ativa): a antiga deixa de valer na hora.</summary>
    public async Task<GroupDetailDto> RegenerateInviteCodeAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanManageInvite(me.Role));

        group.ReplaceInviteCode(await NewInviteCodeAsync(cancellationToken), time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return await access.DetailAsync(group, me, cancellationToken);
    }

    /// <summary>Exclusão lógica: o grupo some para todos e a senha deixa de valer; o histórico continua no banco.</summary>
    public async Task DeleteAsync(Guid userId, Guid groupId, DeleteGroupRequest request, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanDeleteGroup(me.Role));

        if (!string.Equals(request.Confirmation?.Trim(), DeleteConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            throw AppException.Validation(
                "group.delete_not_confirmed",
                $"Para excluir o grupo, envie a confirmação \"{DeleteConfirmation}\".",
                new Dictionary<string, string[]> { ["confirmation"] = [$"Digite {DeleteConfirmation} para confirmar."] });
        }

        group.MarkDeleted(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>O que a senha mostra antes de entrar: nome, tamanho e os perfis sem conta que dá para assumir.</summary>
    public async Task<GroupPreviewDto> LookupAsync(Guid userId, LookupGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await FindByInviteCodeAsync(request.Code, cancellationToken);

        var alreadyMember = await db.GroupMembers.AnyAsync(
            m => m.GroupId == group.Id && m.UserId == userId && m.Status == MemberStatus.Active,
            cancellationToken);

        var members = await access.MembersAsync(group.Id, userId, cancellationToken);
        var claimable = members
            .Where(m => !m.HasAccount)
            .Select(m => new ClaimableMemberDto(m.Id, m.DisplayName, m.Avatar))
            .ToList();

        return new GroupPreviewDto(group.Name, members.Count, alreadyMember, claimable);
    }

    /// <summary>
    /// Entra no grupo com a senha. Quem já é membro recebe o grupo como está (a chamada é idempotente); quem já esteve e saiu volta
    /// como membro comum, com o mesmo histórico. Com <see cref="JoinGroupRequest.ClaimMemberId"/>, assume um perfil sem conta.
    /// </summary>
    public async Task<GroupDetailDto> JoinAsync(Guid userId, JoinGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await FindByInviteCodeAsync(request.Code, cancellationToken);
        var now = time.GetUtcNow();

        var existing = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == group.Id && m.UserId == userId, cancellationToken);

        if (existing is { IsActive: true })
        {
            if (request.ClaimMemberId is not null)
            {
                throw AppException.Conflict("group.already_member", "Você já participa deste grupo.");
            }

            return await access.DetailAsync(group, existing, cancellationToken);
        }

        if (existing is not null && request.ClaimMemberId is not null)
        {
            throw AppException.Conflict(
                "group.claim_unavailable",
                "Você já participou deste grupo e não pode assumir um perfil agora. Entre sem escolher um perfil.");
        }

        await access.EnsureBelowGroupLimitAsync(userId, cancellationToken);

        // Assumir um perfil troca um membro por outro: o tamanho do grupo não muda, então um grupo cheio ainda aceita.
        if (request.ClaimMemberId is null)
        {
            await access.EnsureRoomAsync(group.Id, cancellationToken);
        }

        GroupMember member;
        Guid? previousPhoto = null;

        if (request.ClaimMemberId is { } claimId)
        {
            member = await db.GroupMembers.SingleOrDefaultAsync(
                m => m.Id == claimId && m.GroupId == group.Id && m.UserId == null && m.Status == MemberStatus.Active,
                cancellationToken)
                ?? throw ClaimUnavailable();

            previousPhoto = member.AvatarPhotoId;
            member.Claim(userId, now);
        }
        else if (existing is not null)
        {
            member = existing;
            member.Rejoin(now);
        }
        else
        {
            member = GroupMember.Join(group.Id, userId, now);
            db.GroupMembers.Add(member);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra pessoa assumiu o mesmo perfil no mesmo instante.
            throw ClaimUnavailable();
        }
        catch (DbUpdateException exception) when (dbErrors.IsUniqueViolation(exception, "ux_group_members_group_user"))
        {
            // Duas chamadas entrando ao mesmo tempo; a outra venceu. Repetir é seguro (a entrada é idempotente).
            throw GroupAccess.ConcurrentUpdate();
        }

        if (previousPhoto is { } photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return await access.DetailAsync(group, member, cancellationToken);
    }

    public async Task LeaveAsync(Guid userId, Guid groupId, CancellationToken cancellationToken)
    {
        var (_, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);

        me.Leave(time.GetUtcNow());
        await access.SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Passo da exclusão de conta (a gravação é do chamador): quem é dono de grupo com outras pessoas precisa transferir a
    /// propriedade antes; os grupos em que a pessoa era a única com conta são excluídos; os demais vínculos são encerrados.
    /// </summary>
    public async Task PrepareAccountDeletionAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var owned = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active && m.Role == GroupRole.Owner)
            .Join(db.Groups.Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (member, group) => group)
            .ToListAsync(cancellationToken);

        var blocking = new List<string>();
        var alone = new List<Group>();

        foreach (var group in owned)
        {
            var others = await db.GroupMembers.CountAsync(
                m => m.GroupId == group.Id && m.Status == MemberStatus.Active && m.UserId != null && m.UserId != userId,
                cancellationToken);

            if (others > 0)
            {
                blocking.Add(group.Name);
            }
            else
            {
                alone.Add(group);
            }
        }

        if (blocking.Count > 0)
        {
            throw new AppException(
                ErrorKind.Conflict,
                "user.owns_groups",
                "Você é dono de grupos que têm outras pessoas. Transfira a propriedade de cada um (ou exclua o grupo) antes de excluir a conta.",
                new Dictionary<string, string[]> { ["groups"] = [.. blocking] });
        }

        foreach (var group in alone)
        {
            group.MarkDeleted(now);
        }

        var memberships = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var membership in memberships)
        {
            membership.ForceLeave(now);
        }
    }

    private async Task<Group> FindByInviteCodeAsync(string? input, CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryNormalize(input, out var code))
        {
            throw InvalidCode();
        }

        // Senha desativada, grupo excluído e código inexistente respondem igual: não se descobre o que existe.
        return await db.Groups.AsNoTracking()
            .SingleOrDefaultAsync(g => g.InviteCode == code && g.InviteEnabled && g.DeletedAt == null, cancellationToken)
            ?? throw InvalidCode();
    }

    private async Task<string> NewInviteCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = InviteCodes.Generate();
            if (!await db.Groups.AnyAsync(g => g.InviteCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw AppException.Unavailable("group.code_generation_failed", "Não foi possível gerar a senha do grupo agora. Tente de novo.");
    }

    private static AppException InvalidCode() =>
        AppException.NotFound("group.invalid_code", "Senha do grupo inválida ou desativada.");

    private static AppException ClaimUnavailable() =>
        AppException.Conflict("group.claim_unavailable", "Este perfil não está disponível para ser assumido.");
}
