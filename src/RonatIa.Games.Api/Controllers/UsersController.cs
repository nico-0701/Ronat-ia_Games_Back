using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RonatIa.Games.Api.Filters;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Users;

namespace RonatIa.Games.Api.Controllers;

/// <summary>A própria pessoa: perfil, avatar e exclusão da conta. A identidade vem sempre do token.</summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(UserService users) : ControllerBase
{
    /// <summary>Perfil da própria pessoa.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken) =>
        await users.GetMeAsync(User.RequireUserId(), cancellationToken);

    /// <summary>Altera o nome e/ou escolhe um avatar pronto (só os campos informados mudam).</summary>
    [HttpPatch("me")]
    public async Task<ActionResult<UserDto>> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken) =>
        await users.UpdateMeAsync(User.RequireUserId(), request, cancellationToken);

    /// <summary>
    /// Envia uma foto de avatar (JPEG, PNG, WebP ou GIF, até 3 MB). O servidor recorta em quadrado, reduz para 256x256,
    /// reencoda em WebP e descarta os metadados (inclusive a localização do GPS). Campo do formulário: <c>file</c>.
    /// </summary>
    [HttpPut("me/avatar")]
    [Consumes("multipart/form-data")]
    [AvatarUploadLimit]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    public async Task<ActionResult<UserDto>> SetAvatar(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return await users.SetPhotoAsync(User.RequireUserId(), stream, cancellationToken);
    }

    /// <summary>Remove a foto e volta para o avatar padrão.</summary>
    [HttpDelete("me/avatar")]
    public async Task<ActionResult<UserDto>> ClearAvatar(CancellationToken cancellationToken) =>
        await users.ClearPhotoAsync(User.RequireUserId(), cancellationToken);

    /// <summary>
    /// Exclui a conta (ação definitiva, LGPD): anonimiza os dados, apaga a foto e encerra todas as sessões.
    /// O telefone fica livre para um novo cadastro. Exige <c>{ "confirmation": "EXCLUIR" }</c>.
    /// </summary>
    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        await users.DeleteMeAsync(User.RequireUserId(), request, cancellationToken);
        return NoContent();
    }
}
