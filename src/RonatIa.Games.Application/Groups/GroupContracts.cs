using System.ComponentModel.DataAnnotations;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Application.Groups;

public sealed record CreateGroupRequest
{
    /// <summary>Nome do grupo (2 a 40 caracteres).</summary>
    [Required, StringLength(100)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Alteração parcial do grupo: só os campos informados mudam. Exige ser administrador ou dono.</summary>
public sealed record UpdateGroupRequest
{
    [StringLength(100)]
    public string? Name { get; init; }

    /// <summary>Liga ou desliga a senha do grupo. Desligada, ninguém novo entra, mesmo sabendo o código.</summary>
    public bool? InviteEnabled { get; init; }
}

public sealed record LookupGroupRequest
{
    /// <summary>A senha do grupo, como a pessoa digitou (maiúsculas, hífen e espaços são ignorados).</summary>
    [Required, StringLength(40)]
    public string Code { get; init; } = string.Empty;
}

public sealed record JoinGroupRequest
{
    /// <summary>A senha do grupo, como a pessoa digitou (maiúsculas, hífen e espaços são ignorados).</summary>
    [Required, StringLength(40)]
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Opcional: o perfil sem conta que esta pessoa está assumindo (veja <c>claimableMembers</c> em <c>POST /groups/lookup</c>).
    /// O perfil passa a ser a conta dela e ela herda o histórico.
    /// </summary>
    public Guid? ClaimMemberId { get; init; }
}

public sealed record DeleteGroupRequest
{
    /// <summary>Digite exatamente <c>EXCLUIR</c> para confirmar.</summary>
    [Required, StringLength(20)]
    public string Confirmation { get; init; } = string.Empty;
}

/// <summary>Cria um membro sem conta (perfil com nome e avatar), gerenciado por administradores.</summary>
public sealed record AddProfileRequest
{
    /// <summary>Nome do perfil (2 a 30 caracteres).</summary>
    [Required, StringLength(100)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Avatar pronto (padrão se omitido); a foto é enviada depois, em <c>PUT /groups/{id}/members/{memberId}/avatar</c>.</summary>
    [StringLength(20)]
    public string? AvatarPreset { get; init; }
}

/// <summary>Alteração parcial de um membro: nome/avatar só valem para perfis sem conta; o papel só o dono muda.</summary>
public sealed record UpdateMemberRequest
{
    [StringLength(100)]
    public string? DisplayName { get; init; }

    [StringLength(20)]
    public string? AvatarPreset { get; init; }

    /// <summary><c>admin</c> ou <c>member</c>. Para passar a propriedade, use <c>transfer-ownership</c>.</summary>
    public GroupRole? Role { get; init; }
}

public sealed record TransferOwnershipRequest
{
    /// <summary>O novo dono: outro membro ativo, com conta.</summary>
    [Required]
    public Guid? MemberId { get; init; }
}

/// <param name="Id">Identifica o membro no grupo (e no histórico de partidas); não é o id da conta.</param>
/// <param name="DisplayName">Nome da conta ou do perfil sem conta.</param>
/// <param name="HasAccount">Falso para um perfil sem conta, que alguém pode reivindicar ao entrar.</param>
/// <param name="IsMe">Este membro é a própria pessoa que consultou.</param>
public sealed record MemberDto(Guid Id, string DisplayName, AvatarDto Avatar, GroupRole Role, bool HasAccount, bool IsMe, DateTimeOffset JoinedAt);

public sealed record GroupSummaryDto(Guid Id, string Name, GroupRole MyRole, int MemberCount, DateTimeOffset CreatedAt);

/// <param name="MyRole">O papel de quem consultou.</param>
/// <param name="MyMemberId">O membro de quem consultou neste grupo.</param>
/// <param name="InviteCode">A senha do grupo (8 caracteres) para compartilhar; nulo enquanto a senha estiver desativada.</param>
public sealed record GroupDetailDto(
    Guid Id,
    string Name,
    GroupRole MyRole,
    Guid MyMemberId,
    string? InviteCode,
    bool InviteEnabled,
    IReadOnlyList<MemberDto> Members,
    DateTimeOffset CreatedAt);

/// <summary>Um perfil sem conta que a pessoa pode assumir ao entrar.</summary>
public sealed record ClaimableMemberDto(Guid Id, string DisplayName, AvatarDto Avatar);

/// <summary>O que a senha do grupo revela antes de entrar: nome, tamanho e perfis que podem ser assumidos.</summary>
public sealed record GroupPreviewDto(string Name, int MemberCount, bool AlreadyMember, IReadOnlyList<ClaimableMemberDto> ClaimableMembers);
