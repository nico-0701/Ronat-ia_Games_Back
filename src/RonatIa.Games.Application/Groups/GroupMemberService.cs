using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Users;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Groups;

/// <summary>
/// Membros de um grupo: perfis sem conta (criar, renomear, trocar avatar ou foto), papéis, remoção e transferência de
/// propriedade. A permissão de cada ação vem de <see cref="GroupPermissions"/>; quem não é membro do grupo recebe 404.
/// </summary>
public sealed class GroupMemberService(
    IAppDbContext db,
    GroupAccess access,
    AvatarService avatars,
    TimeProvider time)
{
    public async Task<MemberDto> AddProfileAsync(Guid userId, Guid groupId, AddProfileRequest request, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanManageProfiles(me.Role));

        var profile = GroupMember.CreateProfile(group.Id, request.DisplayName, request.AvatarPreset, time.GetUtcNow());
        await access.EnsureRoomAsync(group.Id, cancellationToken);

        db.GroupMembers.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        return await access.MemberAsync(profile, userId, cancellationToken);
    }

    public async Task<MemberDto> UpdateAsync(Guid userId, Guid groupId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var (_, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        var target = await access.RequireActiveMemberAsync(groupId, memberId, cancellationToken);
        var now = time.GetUtcNow();

        var changesProfile = request.DisplayName is not null || request.AvatarPreset is not null;
        if (changesProfile)
        {
            GroupAccess.Require(GroupPermissions.CanManageProfiles(me.Role));
        }

        if (request.Role is not null)
        {
            GroupAccess.Require(GroupPermissions.CanChangeRoles(me.Role));
        }

        var previousPhoto = target.AvatarPhotoId;

        if (request.DisplayName is not null)
        {
            target.RenameProfile(request.DisplayName, now);
        }

        if (request.AvatarPreset is not null)
        {
            target.UseAvatarPreset(request.AvatarPreset, now);
        }

        if (request.Role is { } role)
        {
            ApplyRole(target, role, now);
        }

        await access.SaveAsync(cancellationToken);

        if (previousPhoto is { } photo && target.AvatarPhotoId != photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return await access.MemberAsync(target, userId, cancellationToken);
    }

    /// <summary>Foto de um perfil sem conta (a de quem tem conta é a da própria conta). A anterior é apagada.</summary>
    public async Task<MemberDto> SetPhotoAsync(Guid userId, Guid groupId, Guid memberId, Stream image, CancellationToken cancellationToken)
    {
        var (_, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanManageProfiles(me.Role));

        var target = await access.RequireActiveMemberAsync(groupId, memberId, cancellationToken);
        RequireProfile(target);

        var previousPhoto = target.AvatarPhotoId;
        var avatar = await avatars.StoreAsync(userId, image, cancellationToken);
        target.UseAvatarPhoto(avatar.Id, time.GetUtcNow());
        await access.SaveAsync(cancellationToken);

        if (previousPhoto is { } photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return await access.MemberAsync(target, userId, cancellationToken);
    }

    public async Task<MemberDto> ClearPhotoAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var (_, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanManageProfiles(me.Role));

        var target = await access.RequireActiveMemberAsync(groupId, memberId, cancellationToken);
        RequireProfile(target);

        if (target.AvatarPhotoId is { } photo)
        {
            target.UseAvatarPreset(AvatarPresets.Default, time.GetUtcNow());
            await access.SaveAsync(cancellationToken);
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return await access.MemberAsync(target, userId, cancellationToken);
    }

    /// <summary>Remove um membro (o histórico dele fica). Quem remove a si mesmo está saindo do grupo.</summary>
    public async Task RemoveAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var (_, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        var target = await access.RequireActiveMemberAsync(groupId, memberId, cancellationToken);
        var now = time.GetUtcNow();

        if (target.Id == me.Id)
        {
            me.Leave(now);
            await access.SaveAsync(cancellationToken);
            return;
        }

        GroupAccess.Require(GroupPermissions.CanRemove(me.Role, target.Role));

        var previousPhoto = target.AvatarPhotoId;
        target.Remove(now);
        await access.SaveAsync(cancellationToken);

        if (previousPhoto is { } photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }
    }

    /// <summary>
    /// Passa a propriedade para outro membro com conta; o dono atual vira administrador. São dois passos numa transação
    /// (rebaixa, depois promove): o índice único do banco só admite um dono ativo por grupo.
    /// </summary>
    public async Task<GroupDetailDto> TransferOwnershipAsync(Guid userId, Guid groupId, TransferOwnershipRequest request, CancellationToken cancellationToken)
    {
        var (group, me) = await access.RequireMemberAsync(userId, groupId, cancellationToken);
        GroupAccess.Require(GroupPermissions.CanTransferOwnership(me.Role));

        var target = await access.RequireActiveMemberAsync(groupId, request.MemberId ?? Guid.Empty, cancellationToken);
        if (target.Id == me.Id || !target.HasAccount)
        {
            throw AppException.Conflict(
                "group.transfer_invalid",
                "A propriedade só pode passar para outra pessoa do grupo que tenha conta.");
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        me.StepDownToAdmin(now);
        await access.SaveAsync(cancellationToken);

        target.MakeOwner(now);
        await access.SaveAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await access.DetailAsync(group, me, cancellationToken);
    }

    private static void ApplyRole(GroupMember target, GroupRole role, DateTimeOffset now)
    {
        if (role == GroupRole.Owner)
        {
            throw AppException.Validation(
                "group.invalid_role",
                "Para passar a propriedade, use a transferência de propriedade.",
                new Dictionary<string, string[]> { ["role"] = ["Use admin ou member."] });
        }

        if (target.Role == GroupRole.Owner)
        {
            throw AppException.Conflict(
                "group.owner_role_fixed",
                "O dono só deixa de ser dono transferindo a propriedade para outra pessoa.");
        }

        if (role == GroupRole.Admin)
        {
            target.MakeAdmin(now);
        }
        else
        {
            target.MakeMember(now);
        }
    }

    private static void RequireProfile(GroupMember target)
    {
        if (target.HasAccount)
        {
            throw AppException.Conflict("member.has_account", "Quem tem conta muda o próprio nome e avatar no perfil.");
        }
    }
}
