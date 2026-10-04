namespace RonatIa.Games.Application.Options;

/// <summary>Cloudflare Turnstile (anti-bot), seção <c>Turnstile</c>. Sem <see cref="SecretKey"/> a verificação fica desligada.</summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>Chave secreta (servidor). Nunca vai para o cliente.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Chave pública do widget; é informada aos clientes por <c>GET /api/v1/meta</c>.</summary>
    public string? SiteKey { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);
}
