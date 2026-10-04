namespace RonatIa.Games.Domain.Users;

/// <summary>
/// Uma sessão de login (um aparelho). Guarda só o <b>hash</b> do refresh token; a cada uso o token é trocado (rotação),
/// e o anterior fica em <see cref="PreviousTokenHash"/> para detectar reutilização (sinal de token roubado).
/// </summary>
public sealed class AuthSession
{
    public const string ReasonLogout = "logout";
    public const string ReasonLogoutAll = "logout_all";
    public const string ReasonReuseDetected = "reuse_detected";
    public const string ReasonUserDeleted = "user_deleted";
    public const string ReasonAdmin = "admin";

    private AuthSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public byte[]? PreviousTokenHash { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    public string? DeviceLabel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public static AuthSession Start(Guid userId, byte[] tokenHash, string? deviceLabel, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserId = userId,
        TokenHash = tokenHash,
        DeviceLabel = Truncate(deviceLabel, 100),
        CreatedAt = now,
        LastUsedAt = now,
        ExpiresAt = now + lifetime,
    };

    /// <summary>Troca o refresh token: o atual vira o "anterior" e a validade é renovada.</summary>
    public void Rotate(byte[] newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        PreviousTokenHash = TokenHash;
        TokenHash = newTokenHash;
        RotatedAt = now;
        LastUsedAt = now;
        ExpiresAt = now + lifetime;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
