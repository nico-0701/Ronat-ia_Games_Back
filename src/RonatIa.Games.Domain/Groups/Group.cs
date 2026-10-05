using RonatIa.Games.Domain.Common;

namespace RonatIa.Games.Domain.Groups;

/// <summary>
/// Grupo de amigos. Quem tem a <see cref="InviteCode"/> (a "senha do grupo") entra nele. Excluir é <b>lógico</b>
/// (<see cref="DeletedAt"/>): as partidas e os placares do grupo continuam íntegros no banco.
/// </summary>
public sealed class Group
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 40;

    private Group()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>A senha do grupo (8 caracteres; ver <see cref="InviteCodes"/>).</summary>
    public string InviteCode { get; private set; } = string.Empty;

    /// <summary>Com a senha desativada ninguém novo entra, mesmo sabendo o código.</summary>
    public bool InviteEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Group Create(string name, string inviteCode, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Name = NormalizeName(name),
        InviteCode = inviteCode,
        InviteEnabled = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static string NormalizeName(string? input) =>
        NameRules.Normalize(input, "name", NameMinLength, NameMaxLength, "group.name_invalid", "O nome do grupo");

    public void Rename(string name, DateTimeOffset now)
    {
        Name = NormalizeName(name);
        UpdatedAt = now;
    }

    /// <summary>Troca a senha e a reativa (a antiga deixa de valer na hora).</summary>
    public void ReplaceInviteCode(string inviteCode, DateTimeOffset now)
    {
        InviteCode = inviteCode;
        InviteEnabled = true;
        UpdatedAt = now;
    }

    public void SetInviteEnabled(bool enabled, DateTimeOffset now)
    {
        InviteEnabled = enabled;
        UpdatedAt = now;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        DeletedAt = now;
        InviteEnabled = false;
        UpdatedAt = now;
    }
}
