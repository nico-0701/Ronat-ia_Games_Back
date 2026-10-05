namespace RonatIa.Games.Application.Options;

/// <summary>Limites dos grupos (seção <c>Groups</c>). Protegem o banco gratuito e a memória, não são regra de negócio.</summary>
public sealed class GroupOptions
{
    public const string SectionName = "Groups";

    /// <summary>Grupos ativos por pessoa (como dono ou membro).</summary>
    public int MaxGroupsPerUser { get; set; } = 20;

    /// <summary>Membros ativos por grupo, contando os perfis sem conta.</summary>
    public int MaxMembersPerGroup { get; set; } = 100;
}
