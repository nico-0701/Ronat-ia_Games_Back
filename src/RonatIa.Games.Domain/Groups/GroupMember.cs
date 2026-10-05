using RonatIa.Games.Domain.Common;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Domain.Groups;

/// <summary>
/// Participante de um grupo: uma pessoa com conta (<see cref="UserId"/>) ou um <b>perfil sem conta</b>, criado e mantido pelo
/// dono/administrador (nome e foto próprios) e que a pessoa pode reivindicar ao entrar com a senha do grupo
/// (<see cref="Claim"/>), herdando o histórico. Quem tem conta usa o nome e o avatar da própria conta.
/// A linha nunca é apagada: sair ou ser removido só muda o <see cref="Status"/>.
/// </summary>
public sealed class GroupMember
{
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 30;

    private GroupMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid GroupId { get; private set; }

    /// <summary>A conta da pessoa; nulo num perfil sem conta.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Nome do perfil sem conta (quem tem conta usa o nome da conta).</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Avatar pronto do perfil sem conta; nulo quando há foto (ou quando o membro tem conta).</summary>
    public string? AvatarPreset { get; private set; }

    /// <summary>Foto do perfil sem conta; nulo quando há avatar pronto (ou quando o membro tem conta).</summary>
    public Guid? AvatarPhotoId { get; private set; }

    public GroupRole Role { get; private set; }

    public MemberStatus Status { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset? LeftAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concorrência otimista: duas pessoas reivindicando o mesmo perfil, ou duas trocas de papel, não gravam as duas.</summary>
    public int Version { get; private set; }

    public bool HasAccount => UserId is not null;

    public bool IsActive => Status == MemberStatus.Active;

    public static GroupMember CreateOwner(Guid groupId, Guid userId, DateTimeOffset now) =>
        ForUser(groupId, userId, GroupRole.Owner, now);

    public static GroupMember Join(Guid groupId, Guid userId, DateTimeOffset now) =>
        ForUser(groupId, userId, GroupRole.Member, now);

    public static GroupMember CreateProfile(Guid groupId, string displayName, string? avatarPreset, DateTimeOffset now)
    {
        var preset = avatarPreset ?? AvatarPresets.Default;
        if (!AvatarPresets.IsValid(preset))
        {
            throw UnknownPreset();
        }

        return new GroupMember
        {
            Id = Guid.CreateVersion7(now),
            GroupId = groupId,
            DisplayName = NormalizeDisplayName(displayName),
            AvatarPreset = preset,
            Role = GroupRole.Member,
            Status = MemberStatus.Active,
            JoinedAt = now,
            UpdatedAt = now,
        };
    }

    public static string NormalizeDisplayName(string? input) =>
        NameRules.Normalize(input, "displayName", DisplayNameMinLength, DisplayNameMaxLength, "member.display_name_invalid", "O nome");

    /// <summary>Quem já esteve no grupo (saiu ou foi removido) volta como membro comum, com o mesmo histórico.</summary>
    public void Rejoin(DateTimeOffset now)
    {
        Status = MemberStatus.Active;
        Role = GroupRole.Member;
        LeftAt = null;
        Touch(now);
    }

    public void Leave(DateTimeOffset now)
    {
        if (Role == GroupRole.Owner)
        {
            throw AppException.Conflict(
                "group.owner_cannot_leave",
                "O dono não pode sair do grupo. Transfira a propriedade para outra pessoa ou exclua o grupo.");
        }

        End(MemberStatus.Left, now);
    }

    /// <summary>Encerra o vínculo por exclusão da conta, inclusive de quem era dono (o grupo já foi excluído ou a propriedade já passou).</summary>
    public void ForceLeave(DateTimeOffset now)
    {
        Role = GroupRole.Member;
        End(MemberStatus.Left, now);
    }

    public void Remove(DateTimeOffset now)
    {
        if (Role == GroupRole.Owner)
        {
            throw AppException.Conflict(
                "group.owner_cannot_leave",
                "O dono não pode ser removido. Transfira a propriedade para outra pessoa ou exclua o grupo.");
        }

        End(MemberStatus.Removed, now);

        // Um perfil removido deixa de guardar foto (o nome fica, pelo histórico).
        if (!HasAccount && AvatarPhotoId is not null)
        {
            AvatarPhotoId = null;
            AvatarPreset = AvatarPresets.Default;
        }
    }

    /// <summary>A pessoa assume o perfil sem conta: o nome e o avatar passam a ser os da conta dela.</summary>
    public void Claim(Guid userId, DateTimeOffset now)
    {
        if (HasAccount || !IsActive)
        {
            throw AppException.Conflict("group.claim_unavailable", "Este perfil não está disponível para reivindicação.");
        }

        UserId = userId;
        DisplayName = null;
        AvatarPreset = null;
        AvatarPhotoId = null;
        Touch(now);
    }

    public void MakeAdmin(DateTimeOffset now) => SetRole(GroupRole.Admin, now);

    public void MakeMember(DateTimeOffset now) => SetRole(GroupRole.Member, now);

    /// <summary>Usado só na transferência de propriedade (antes, o dono atual é rebaixado a administrador).</summary>
    public void MakeOwner(DateTimeOffset now) => SetRole(GroupRole.Owner, now);

    public void StepDownToAdmin(DateTimeOffset now)
    {
        Role = GroupRole.Admin;
        Touch(now);
    }

    public void RenameProfile(string displayName, DateTimeOffset now)
    {
        RequireProfile();
        DisplayName = NormalizeDisplayName(displayName);
        Touch(now);
    }

    public void UseAvatarPreset(string preset, DateTimeOffset now)
    {
        RequireProfile();
        if (!AvatarPresets.IsValid(preset))
        {
            throw UnknownPreset();
        }

        AvatarPreset = preset;
        AvatarPhotoId = null;
        Touch(now);
    }

    public void UseAvatarPhoto(Guid photoId, DateTimeOffset now)
    {
        RequireProfile();
        AvatarPhotoId = photoId;
        AvatarPreset = null;
        Touch(now);
    }

    private static GroupMember ForUser(Guid groupId, Guid userId, GroupRole role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        GroupId = groupId,
        UserId = userId,
        Role = role,
        Status = MemberStatus.Active,
        JoinedAt = now,
        UpdatedAt = now,
    };

    private void SetRole(GroupRole role, DateTimeOffset now)
    {
        if (!HasAccount)
        {
            throw AppException.Conflict("member.no_account", "Só quem tem conta pode ter um papel de administração.");
        }

        Role = role;
        Touch(now);
    }

    private void End(MemberStatus status, DateTimeOffset now)
    {
        Status = status;
        LeftAt = now;
        Touch(now);
    }

    private void RequireProfile()
    {
        if (HasAccount)
        {
            throw AppException.Conflict("member.has_account", "Quem tem conta muda o próprio nome e avatar no perfil.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static AppException UnknownPreset() =>
        AppException.Validation(
            "avatar.unknown_preset",
            "Avatar desconhecido.",
            new Dictionary<string, string[]> { ["avatar"] = ["Escolha um dos avatares disponíveis."] });
}
