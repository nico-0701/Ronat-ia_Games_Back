using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Options;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>Os valores públicos de desenvolvimento só valem em Development e nos testes; em produção a API não sobe com eles.</summary>
internal static class SecretPolicy
{
    public static bool AllowsDevelopmentSecrets(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

internal sealed class AuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (!JwtKeys.TryDecode(options.PhonePepper, out var pepper) || pepper.Length < JwtKeys.MinimumKeyBytes)
        {
            failures.Add("Auth:PhonePepper deve ser base64 com pelo menos 32 bytes (gere com: openssl rand -base64 48).");
        }
        else if (options.PhonePepper == DevelopmentSecrets.PhonePepper && !SecretPolicy.AllowsDevelopmentSecrets(environment))
        {
            failures.Add("Auth:PhonePepper é o valor público de desenvolvimento e não pode ser usado fora de Development.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultRegion) || options.DefaultRegion.Length != 2)
        {
            failures.Add("Auth:DefaultRegion deve ser um código de país de 2 letras (ex.: BR).");
        }

        if (options.RefreshTokenLifetime <= TimeSpan.Zero)
        {
            failures.Add("Auth:RefreshTokenLifetime deve ser positivo.");
        }

        if (options.RefreshReuseGrace < TimeSpan.Zero)
        {
            failures.Add("Auth:RefreshReuseGrace não pode ser negativo.");
        }

        if (options.MaxSessionsPerUser < 1)
        {
            failures.Add("Auth:MaxSessionsPerUser deve ser pelo menos 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (!JwtKeys.TryDecode(options.SigningKey, out var key) || key.Length < JwtKeys.MinimumKeyBytes)
        {
            failures.Add("Jwt:SigningKey deve ser base64 com pelo menos 32 bytes (gere com: openssl rand -base64 64).");
        }
        else if (options.SigningKey == DevelopmentSecrets.JwtSigningKey && !SecretPolicy.AllowsDevelopmentSecrets(environment))
        {
            failures.Add("Jwt:SigningKey é o valor público de desenvolvimento e não pode ser usado fora de Development.");
        }

        if (!string.IsNullOrWhiteSpace(options.PreviousSigningKey)
            && (!JwtKeys.TryDecode(options.PreviousSigningKey, out var previous) || previous.Length < JwtKeys.MinimumKeyBytes))
        {
            failures.Add("Jwt:PreviousSigningKey, quando informada, deve ser base64 com pelo menos 32 bytes.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Issuer e Jwt:Audience são obrigatórios.");
        }

        if (options.AccessTokenLifetime <= TimeSpan.Zero)
        {
            failures.Add("Jwt:AccessTokenLifetime deve ser positivo.");
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            failures.Add("Jwt:ClockSkew não pode ser negativo.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
