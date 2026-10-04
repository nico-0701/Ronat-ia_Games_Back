using System.ComponentModel.DataAnnotations;

namespace RonatIa.Games.Application.Auth;

// Os pedidos usam propriedades (e não parâmetros posicionais) porque o MVC recusa atributos de validação
// com alvo "property:" em records posicionais.

/// <summary>Pedido de login: o telefone é o único dado necessário.</summary>
public sealed record LoginRequest
{
    /// <summary>Telefone, com ou sem código do país e com qualquer máscara (padrão: Brasil).</summary>
    [Required, StringLength(40)]
    public string Phone { get; init; } = string.Empty;

    /// <summary>Token do Cloudflare Turnstile (obrigatório quando o servidor o exige; ver <c>GET /api/v1/meta</c>).</summary>
    [StringLength(2048)]
    public string? CaptchaToken { get; init; }

    /// <summary>Nome do aparelho, para a lista de sessões (opcional).</summary>
    [StringLength(100)]
    public string? DeviceName { get; init; }
}

/// <summary>Primeiro acesso: cria a conta com nome e avatar.</summary>
public sealed record RegisterRequest
{
    [Required, StringLength(40)]
    public string Phone { get; init; } = string.Empty;

    /// <summary>Nome de exibição (2 a 30 caracteres).</summary>
    [Required, StringLength(100)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Chave de um avatar pronto (ex.: <c>preset-3</c>); se omitida, usa o padrão. Foto própria: <c>PUT /api/v1/users/me/avatar</c>.</summary>
    [StringLength(20)]
    public string? AvatarPreset { get; init; }

    /// <summary>Aceite dos termos de uso e da política de privacidade.</summary>
    public bool AcceptTerms { get; init; }

    [StringLength(2048)]
    public string? CaptchaToken { get; init; }

    [StringLength(100)]
    public string? DeviceName { get; init; }
}

public sealed record RefreshRequest
{
    [Required, StringLength(200)]
    public string RefreshToken { get; init; } = string.Empty;

    [StringLength(100)]
    public string? DeviceName { get; init; }
}

/// <param name="Kind"><c>preset</c> (avatar pronto) ou <c>photo</c> (foto enviada).</param>
/// <param name="Preset">Chave do avatar pronto, quando <paramref name="Kind"/> é <c>preset</c>.</param>
/// <param name="Url">Caminho relativo da foto (prefixe com a URL base da API), quando <paramref name="Kind"/> é <c>photo</c>.</param>
public sealed record AvatarDto(string Kind, string? Preset, string? Url);

/// <param name="PhoneLast4">Últimos 4 dígitos do telefone, só para a própria pessoa se reconhecer. O número completo nunca é guardado.</param>
public sealed record UserDto(Guid Id, string DisplayName, AvatarDto Avatar, string PhoneLast4, DateTimeOffset CreatedAt);

/// <param name="AccessToken">JWT curto. Envie em <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Token opaco para obter um novo par em <c>POST /api/v1/auth/refresh</c>; é trocado a cada uso.</param>
/// <param name="IsNewUser"><c>true</c> quando a conta acabou de ser criada.</param>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool IsNewUser,
    UserDto User);

/// <summary>Um aparelho conectado.</summary>
public sealed record SessionDto(Guid Id, string? DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, bool IsCurrent);

/// <summary>Contexto da chamada, para registrar o aparelho e verificar o captcha.</summary>
public sealed record ClientContext(string? IpAddress, string? DeviceLabel);
