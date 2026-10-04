using System.Security.Cryptography;
using RonatIa.Games.Domain.Common;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Domain.Users;

/// <summary>
/// Conta de uma pessoa. O telefone nunca é guardado em claro: só o HMAC dele (<see cref="PhoneHash"/>, usado como
/// identificador único) e os 4 últimos dígitos, para exibição. O avatar é um preset <b>ou</b> uma foto enviada.
/// </summary>
public sealed class User
{
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 30;
    public const string DeletedDisplayName = "Jogador removido";

    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>HMAC-SHA256 do telefone em E.164 (32 bytes).</summary>
    public byte[] PhoneHash { get; private set; } = [];

    public string PhoneLast4 { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Chave do avatar pronto (ex.: <c>preset-2</c>); nulo quando há foto.</summary>
    public string? AvatarPreset { get; private set; }

    /// <summary>Foto enviada pela pessoa; nulo quando há preset.</summary>
    public Guid? AvatarPhotoId { get; private set; }

    public UserStatus Status { get; private set; }

    public bool IsAdmin { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? TermsAcceptedAt { get; private set; }

    public string? TermsVersion { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static User Register(
        byte[] phoneHash,
        string phoneLast4,
        string displayName,
        string? avatarPreset,
        string termsVersion,
        DateTimeOffset now)
    {
        var preset = avatarPreset ?? AvatarPresets.Default;
        if (!AvatarPresets.IsValid(preset))
        {
            throw UnknownPreset();
        }

        return new User
        {
            Id = Guid.CreateVersion7(now),
            PhoneHash = phoneHash,
            PhoneLast4 = phoneLast4,
            DisplayName = NormalizeDisplayName(displayName),
            AvatarPreset = preset,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now,
            TermsAcceptedAt = now,
            TermsVersion = termsVersion,
        };
    }

    public static string NormalizeDisplayName(string? input) =>
        NameRules.Normalize(input, "displayName", DisplayNameMinLength, DisplayNameMaxLength, "user.display_name_invalid", "O nome");

    public void Rename(string displayName, DateTimeOffset now)
    {
        DisplayName = NormalizeDisplayName(displayName);
        UpdatedAt = now;
    }

    public void UseAvatarPreset(string preset, DateTimeOffset now)
    {
        if (!AvatarPresets.IsValid(preset))
        {
            throw UnknownPreset();
        }

        AvatarPreset = preset;
        AvatarPhotoId = null;
        UpdatedAt = now;
    }

    public void UseAvatarPhoto(Guid photoId, DateTimeOffset now)
    {
        AvatarPhotoId = photoId;
        AvatarPreset = null;
        UpdatedAt = now;
    }

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void Suspend(DateTimeOffset now)
    {
        Status = UserStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            throw AppException.Conflict("user.deleted", "Esta conta foi excluída.");
        }

        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>
    /// Exclusão (LGPD): anonimiza a conta mas preserva a linha, para o histórico de partidas continuar íntegro.
    /// O hash do telefone vira um valor aleatório, o que libera o número para um novo cadastro.
    /// </summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        Status = UserStatus.Deleted;
        DisplayName = DeletedDisplayName;
        PhoneHash = RandomNumberGenerator.GetBytes(32);
        PhoneLast4 = "0000";
        AvatarPreset = AvatarPresets.Default;
        AvatarPhotoId = null;
        IsAdmin = false;
        DeletedAt = now;
        UpdatedAt = now;
    }

    private static AppException UnknownPreset() =>
        AppException.Validation(
            "avatar.unknown_preset",
            "Avatar desconhecido.",
            new Dictionary<string, string[]> { ["avatar"] = ["Escolha um dos avatares disponíveis."] });
}
