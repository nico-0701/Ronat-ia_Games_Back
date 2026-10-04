using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>
/// Emite o JWT de acesso (HS256). Só carrega o necessário: <c>sub</c> (id da conta), <c>sid</c> (id da sessão de login) e,
/// para administradores, <c>role</c>. Nada de telefone, nome ou outro dado pessoal.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public const string SessionIdClaim = "sid";

    private readonly JsonWebTokenHandler _handler = new();

    private readonly Lazy<SigningCredentials> _credentials =
        new(() => new SigningCredentials(JwtKeys.Current(options.Value), SecurityAlgorithms.HmacSha256));

    public AccessToken Issue(User user, Guid sessionId, DateTimeOffset now)
    {
        var settings = options.Value;
        var expires = now + settings.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [SessionIdClaim] = sessionId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };

        if (user.IsAdmin)
        {
            claims["role"] = "admin";
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _credentials.Value,
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
