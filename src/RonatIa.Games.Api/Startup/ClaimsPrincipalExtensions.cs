using System.Security.Claims;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Infrastructure.Security;

namespace RonatIa.Games.Api.Startup;

/// <summary>Leitura da identidade a partir do token validado. A identidade vem sempre daqui, nunca do corpo da requisição.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) => ParseGuid(principal.FindFirst("sub")?.Value);

    public static Guid? GetSessionId(this ClaimsPrincipal principal) => ParseGuid(principal.FindFirst(JwtTokenService.SessionIdClaim)?.Value);

    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw AppException.Unauthorized("auth.unauthorized", "Não autenticado.");

    public static Guid RequireSessionId(this ClaimsPrincipal principal) =>
        principal.GetSessionId() ?? throw AppException.Unauthorized("auth.unauthorized", "Não autenticado.");

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
