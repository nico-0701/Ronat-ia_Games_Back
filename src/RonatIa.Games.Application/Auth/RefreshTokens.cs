using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace RonatIa.Games.Application.Auth;

/// <summary>
/// Refresh tokens: 256 bits aleatórios em base64url. O servidor guarda apenas o SHA-256; como o token já tem entropia
/// máxima, um hash simples (e rápido) basta, sem sal nem custo de KDF.
/// </summary>
public static class RefreshTokens
{
    private const int ByteLength = 32;

    /// <summary>Tamanho em caracteres de um token válido (32 bytes em base64url, sem preenchimento).</summary>
    public const int TokenLength = 43;

    public static (string Token, byte[] Hash) Generate()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    /// <summary>Descarta cedo (sem consultar o banco) o que nem tem o formato de um refresh token.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: TokenLength } && !token.Contains('=') && Base64Url.IsValid(token);
}
