using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests.Infrastructure;

public static class TestPhones
{
    private static int _counter = Random.Shared.Next(0, 5_000_000);

    /// <summary>Celular brasileiro válido e único dentro do processo de teste (formato E.164).</summary>
    public static string Next()
    {
        var n = Interlocked.Increment(ref _counter);
        return $"+55119{6 + ((n / 10_000_000) % 4)}{n % 10_000_000:D7}";
    }
}

public static class AuthTestHelpers
{
    public static async Task<AuthResponse> RegisterAsync(
        this HttpClient client,
        string? phone = null,
        string name = "Pessoa Teste",
        string? preset = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            phone = phone ?? TestPhones.Next(),
            displayName = name,
            avatarPreset = preset,
            acceptTerms = true,
        });

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static async Task<HttpResponseMessage> LoginRawAsync(this HttpClient client, string phone) =>
        await client.PostAsJsonAsync("/api/v1/auth/login", new { phone });

    public static async Task<AuthResponse> LoginAsync(this HttpClient client, string phone)
    {
        var response = await client.LoginRawAsync(phone);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static async Task<HttpResponseMessage> RefreshRawAsync(this HttpClient client, string refreshToken) =>
        await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    public static async Task<AuthResponse> RefreshAsync(this HttpClient client, string refreshToken)
    {
        var response = await client.RefreshRawAsync(refreshToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    /// <summary>Cria um cliente HTTP novo, já autenticado com o access token.</summary>
    public static HttpClient ClientFor(this WebApplicationFactory<Program> factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string?> ReadCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadProblemAsync()).GetProperty("code").GetString();
}

/// <summary>Fabrica JWTs "de verdade" (assinados com a chave de teste) para checar o que o servidor aceita e recusa.</summary>
public static class TokenForgery
{
    public static string Create(
        string signingKeyBase64,
        Guid? userId = null,
        Guid? sessionId = null,
        string issuer = "ronat-ia-games",
        string audience = "ronat-ia-games-clients",
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = (issuedAt + (lifetime ?? TimeSpan.FromMinutes(10))).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (userId ?? Guid.CreateVersion7()).ToString(),
                ["sid"] = (sessionId ?? Guid.CreateVersion7()).ToString(),
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(signingKeyBase64)), algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>JWT com <c>alg: none</c> (sem assinatura), o ataque clássico de "algoritmo none".</summary>
    public static string CreateUnsigned(Guid userId, Guid sessionId)
    {
        static string B64(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var payload = B64($$"""{"sub":"{{userId}}","sid":"{{sessionId}}","iss":"ronat-ia-games","aud":"ronat-ia-games-clients","exp":{{exp}}}""");
        return $"{header}.{payload}.";
    }
}
