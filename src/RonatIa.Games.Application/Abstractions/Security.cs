using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Abstractions;

/// <summary>Telefone normalizado: <see cref="E164"/> (ex.: <c>+5511988887777</c>) e os 4 últimos dígitos para exibição.</summary>
public sealed record NormalizedPhone(string E164, string Last4);

public interface IPhoneNormalizer
{
    /// <summary>Interpreta o telefone digitado (com ou sem país, com máscara) ou lança <c>auth.invalid_phone</c>.</summary>
    NormalizedPhone Normalize(string? input);
}

public interface IPhoneHasher
{
    /// <summary>HMAC-SHA256 do telefone em E.164, com o segredo do servidor. É o identificador único da conta.</summary>
    byte[] Hash(string e164);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    /// <summary>Emite o JWT de acesso (curto) para a sessão informada.</summary>
    AccessToken Issue(User user, Guid sessionId, DateTimeOffset now);
}

public interface ICaptchaVerifier
{
    bool IsEnabled { get; }

    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}

public interface ISessionValidator
{
    /// <summary>A sessão de login continua válida (não revogada, não expirada) e a conta continua ativa?</summary>
    Task<bool> IsActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Esquece o resultado em cache de uma sessão (chamado quando ela é revogada).</summary>
    void Evict(Guid sessionId);
}

public interface IDbExceptionClassifier
{
    /// <summary>Violação de índice único (opcionalmente de um índice/constraint específico).</summary>
    bool IsUniqueViolation(Exception exception, string? constraintName = null);
}
