using Microsoft.AspNetCore.Mvc;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Users;

namespace RonatIa.Games.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(UserService users) : ControllerBase
{
    /// <summary>Perfil da própria pessoa (a identidade vem do token).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken) =>
        await users.GetMeAsync(User.RequireUserId(), cancellationToken);
}
