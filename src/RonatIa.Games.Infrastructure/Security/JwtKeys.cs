using Microsoft.IdentityModel.Tokens;
using RonatIa.Games.Application.Options;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>Chaves simétricas do JWT: a atual (assina e valida) e, durante uma rotação, a anterior (só valida).</summary>
public static class JwtKeys
{
    public const string CurrentKeyId = "current";
    public const string PreviousKeyId = "previous";
    public const int MinimumKeyBytes = 32;

    public static SymmetricSecurityKey Current(JwtOptions options) => Create(options.SigningKey, CurrentKeyId);

    public static IReadOnlyList<SecurityKey> ForValidation(JwtOptions options)
    {
        var keys = new List<SecurityKey> { Current(options) };
        if (!string.IsNullOrWhiteSpace(options.PreviousSigningKey))
        {
            keys.Add(Create(options.PreviousSigningKey, PreviousKeyId));
        }

        return keys;
    }

    /// <summary>Decodifica base64 sem lançar exceção.</summary>
    public static bool TryDecode(string? base64, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static SymmetricSecurityKey Create(string base64, string keyId) =>
        new(Convert.FromBase64String(base64)) { KeyId = keyId };
}
