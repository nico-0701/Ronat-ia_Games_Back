using Microsoft.AspNetCore.Mvc;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>
/// Endpoint SEM nenhum atributo de autorização: serve para provar que o padrão é negar (política de fallback exige login).
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("test/secure")]
public sealed class TestSecureController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { secret = "só para quem está autenticado" });
}
