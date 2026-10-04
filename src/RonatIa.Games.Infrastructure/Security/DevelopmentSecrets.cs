namespace RonatIa.Games.Infrastructure.Security;

/// <summary>
/// Valores <b>públicos e propositalmente fracos</b>, usados apenas por <c>appsettings.Development.json</c> para a API rodar sem
/// configuração em uma máquina de desenvolvimento. Qualquer ambiente fora de Development (e do de testes) recusa subir com eles.
/// </summary>
public static class DevelopmentSecrets
{
    // base64("dev-only-pepper--never-use-in-production--")
    public const string PhonePepper = "ZGV2LW9ubHktcGVwcGVyLS1uZXZlci11c2UtaW4tcHJvZHVjdGlvbi0t";

    // base64("dev-only-jwt-signing-key--never-use-in-production--0123456789abcdef")
    public const string JwtSigningKey = "ZGV2LW9ubHktand0LXNpZ25pbmcta2V5LS1uZXZlci11c2UtaW4tcHJvZHVjdGlvbi0tMDEyMzQ1Njc4OWFiY2RlZg==";
}
