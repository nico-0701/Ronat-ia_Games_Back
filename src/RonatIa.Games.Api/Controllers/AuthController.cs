using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Controllers;

/// <summary>Entrada e sessões. O telefone é só o identificador; não há SMS nem senha (ver ADR-0003).</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>
    /// Entra com o telefone. Se ainda não existir conta com esse telefone, responde <c>404</c> com o código
    /// <c>auth.user_not_found</c>; o cliente então mostra o cadastro (nome e avatar) e chama <c>register</c>.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) =>
        await auth.LoginAsync(request, Client(request.DeviceName), cancellationToken);

    /// <summary>Cria a conta (telefone, nome e avatar) e já inicia a sessão.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthRegister)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.RegisterAsync(request, Client(request.DeviceName), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Troca o refresh token por um novo par de tokens. O refresh token usado deixa de valer.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthRefresh)]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken) =>
        await auth.RefreshAsync(request, Client(request.DeviceName), cancellationToken);

    /// <summary>Encerra a sessão atual (este aparelho).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(User.RequireUserId(), User.RequireSessionId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Encerra todas as sessões da conta (todos os aparelhos).</summary>
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await auth.LogoutAllAsync(User.RequireUserId(), cancellationToken);
        return NoContent();
    }

    /// <summary>Aparelhos conectados à conta.</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken cancellationToken) =>
        Ok(await auth.ListSessionsAsync(User.RequireUserId(), User.RequireSessionId(), cancellationToken));

    /// <summary>Desconecta um aparelho específico.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(User.RequireUserId(), sessionId, cancellationToken);
        return NoContent();
    }

    private ClientContext Client(string? deviceName) => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        DeviceLabels.Resolve(deviceName, Request.Headers.UserAgent.ToString()));
}
