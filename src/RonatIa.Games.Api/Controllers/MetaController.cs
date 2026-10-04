using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Options;

namespace RonatIa.Games.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/meta")]
public sealed class MetaController(
    IOptions<ClientSettings> client,
    IOptions<RegistrationOptions> registration,
    IOptions<TurnstileOptions> turnstile,
    TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Informações públicas do servidor: versão da API, versão mínima do cliente (abaixo dela o app pede atualização),
    /// a hora do servidor (para sincronizar cronômetros) e o que a tela de entrada precisa saber (cadastro aberto? captcha?).
    /// </summary>
    [HttpGet]
    public ActionResult<MetaResponse> Get() => new MetaResponse(
        "v1",
        client.Value.MinClientVersion,
        time.GetUtcNow(),
        new AuthMeta(
            registration.Value.Mode == RegistrationMode.Open,
            turnstile.Value.Enabled,
            turnstile.Value.Enabled ? turnstile.Value.SiteKey : null));
}

/// <param name="ApiVersion">Versão do contrato da API.</param>
/// <param name="MinClientVersion">Versão mínima do app (SemVer) aceita pelo servidor.</param>
/// <param name="ServerTimeUtc">Hora atual do servidor, em UTC.</param>
public sealed record MetaResponse(string ApiVersion, string MinClientVersion, DateTimeOffset ServerTimeUtc, AuthMeta Auth);

/// <param name="RegistrationOpen">Se <c>false</c>, novos cadastros estão fechados (quem já tem conta continua entrando).</param>
/// <param name="CaptchaRequired">Se <c>true</c>, login e cadastro exigem o token do Cloudflare Turnstile em <c>captchaToken</c>.</param>
/// <param name="CaptchaSiteKey">Chave pública do widget do Turnstile (só quando exigido).</param>
public sealed record AuthMeta(bool RegistrationOpen, bool CaptchaRequired, string? CaptchaSiteKey);
