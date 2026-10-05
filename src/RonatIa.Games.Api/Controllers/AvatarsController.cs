using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RonatIa.Games.Application.Users;

namespace RonatIa.Games.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/avatars")]
public sealed class AvatarsController(AvatarService avatars) : ControllerBase
{
    /// <summary>Opções de avatar pronto para escolher (a tela de cadastro usa isto antes do login).</summary>
    [HttpGet("presets")]
    public ActionResult<AvatarPresetsDto> Presets() => avatars.Presets();

    /// <summary>
    /// Imagem de uma foto de avatar (WebP 256x256). O endereço não é adivinhável e o conteúdo nunca muda, então a resposta é
    /// pública e cacheável por um ano (<c>immutable</c>); suporta <c>ETag</c>/<c>If-None-Match</c>.
    /// </summary>
    [HttpGet("{avatarId:guid}")]
    [Produces("image/webp")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "image/webp")]
    public async Task<IActionResult> Get(Guid avatarId, CancellationToken cancellationToken)
    {
        var content = await avatars.GetContentAsync(avatarId, cancellationToken);

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

        return File(content.Data, content.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"{content.ETag}\""));
    }
}
