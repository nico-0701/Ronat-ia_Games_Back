using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RonatIa.Games.Api.Startup;

namespace RonatIa.Games.Api.Controllers;

[ApiController]
[Route("api/v1/meta")]
public sealed class MetaController(IOptions<ClientSettings> client, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Informações públicas do servidor: versão da API, versão mínima do cliente (abaixo dela o app pede atualização)
    /// e a hora do servidor, usada para sincronizar cronômetros.
    /// </summary>
    [HttpGet]
    public ActionResult<MetaResponse> Get() =>
        new MetaResponse("v1", client.Value.MinClientVersion, time.GetUtcNow());
}

/// <param name="ApiVersion">Versão do contrato da API.</param>
/// <param name="MinClientVersion">Versão mínima do app (SemVer) aceita pelo servidor.</param>
/// <param name="ServerTimeUtc">Hora atual do servidor, em UTC.</param>
public sealed record MetaResponse(string ApiVersion, string MinClientVersion, DateTimeOffset ServerTimeUtc);
