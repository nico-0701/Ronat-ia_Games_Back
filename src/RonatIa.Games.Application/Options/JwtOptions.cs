namespace RonatIa.Games.Application.Options;

/// <summary>Configuração do JWT de acesso (seção <c>Jwt</c>).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Chave de assinatura HS256, em base64 (no mínimo 32 bytes).</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Chave anterior, aceita apenas para validar tokens ainda válidos durante uma rotação de chave.</summary>
    public string? PreviousSigningKey { get; set; }

    public string Issuer { get; set; } = "ronat-ia-games";

    public string Audience { get; set; } = "ronat-ia-games-clients";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
