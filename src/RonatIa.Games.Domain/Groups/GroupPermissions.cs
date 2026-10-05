namespace RonatIa.Games.Domain.Groups;

/// <summary>
/// Quem pode o quê num grupo. A regra é do servidor: o cliente só esconde botões. Dono: tudo. Administrador: gerencia a
/// senha, o nome e os perfis sem conta, e remove membros comuns. Membro: participa e sai.
/// </summary>
public static class GroupPermissions
{
    public static bool CanEditGroup(GroupRole role) => role >= GroupRole.Admin;

    public static bool CanManageInvite(GroupRole role) => role >= GroupRole.Admin;

    /// <summary>Criar, editar e remover membros sem conta (inclusive a foto deles).</summary>
    public static bool CanManageProfiles(GroupRole role) => role >= GroupRole.Admin;

    /// <summary>Promover a administrador e rebaixar a membro.</summary>
    public static bool CanChangeRoles(GroupRole role) => role == GroupRole.Owner;

    public static bool CanTransferOwnership(GroupRole role) => role == GroupRole.Owner;

    public static bool CanDeleteGroup(GroupRole role) => role == GroupRole.Owner;

    /// <summary>O dono nunca é removido (precisa transferir a propriedade ou excluir o grupo); administradores só removem membros comuns.</summary>
    public static bool CanRemove(GroupRole actor, GroupRole target) =>
        target != GroupRole.Owner
        && actor switch
        {
            GroupRole.Owner => true,
            GroupRole.Admin => target == GroupRole.Member,
            _ => false,
        };
}
