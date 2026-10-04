using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests;

/// <summary>O que o servidor aceita e recusa como prova de identidade.</summary>
public sealed class AuthTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static HttpClient WithBearer(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.unauthorized", await response.ReadCodeAsync());
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Endpoints_are_protected_by_default_even_without_authorization_attributes()
    {
        var anonymous = await factory.CreateClient().GetAsync("/test/secure");
        var auth = await factory.CreateClient().RegisterAsync();
        var authenticated = await factory.ClientFor(auth.AccessToken).GetAsync("/test/secure");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/api/v1/meta")]
    [InlineData("/openapi/v1.json")]
    public async Task Infrastructure_and_public_endpoints_do_not_need_a_token(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var parts = auth.AccessToken.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}A.{parts[2]}";

        var response = await WithBearer(factory.CreateClient(), tampered).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var forged = TokenForgery.Create(otherKey, auth.User.Id, Guid.CreateVersion7());

        var response = await WithBearer(factory.CreateClient(), forged).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unsigned_token_with_alg_none_is_rejected()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var unsigned = TokenForgery.CreateUnsigned(auth.User.Id, Guid.CreateVersion7());

        var response = await WithBearer(factory.CreateClient(), unsigned).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_the_right_signature_but_a_wrong_issuer_or_audience_is_rejected()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var wrongIssuer = TokenForgery.Create(factory.JwtSigningKey, auth.User.Id, issuer: "outro-emissor");
        var wrongAudience = TokenForgery.Create(factory.JwtSigningKey, auth.User.Id, audience: "outra-audiencia");

        var issuerResponse = await WithBearer(factory.CreateClient(), wrongIssuer).GetAsync("/api/v1/users/me");
        var audienceResponse = await WithBearer(factory.CreateClient(), wrongAudience).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, issuerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, audienceResponse.StatusCode);
    }

    [Fact]
    public async Task A_correctly_signed_token_for_a_session_that_does_not_exist_is_rejected()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var forged = TokenForgery.Create(factory.JwtSigningKey, auth.User.Id, Guid.CreateVersion7());

        var response = await WithBearer(factory.CreateClient(), forged).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_a_session_of_another_user_is_rejected()
    {
        var victim = await factory.CreateClient().RegisterAsync(name: "Vítima");
        var attacker = await factory.CreateClient().RegisterAsync(name: "Atacante");
        var sessions = await factory.ClientFor(victim.AccessToken).GetFromJsonAsync<List<SessionDto>>("/api/v1/auth/sessions");
        var forged = TokenForgery.Create(factory.JwtSigningKey, attacker.User.Id, sessions![0].Id);

        var response = await WithBearer(factory.CreateClient(), forged).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var auth = await host.CreateClient().RegisterAsync();

        var before = await host.ClientFor(auth.AccessToken).GetAsync("/api/v1/users/me");
        time.Advance(TimeSpan.FromMinutes(29));
        var almost = await host.ClientFor(auth.AccessToken).GetAsync("/api/v1/users/me");
        time.Advance(TimeSpan.FromMinutes(2));
        var after = await host.ClientFor(auth.AccessToken).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, almost.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task The_identity_comes_from_the_token_and_never_from_the_request()
    {
        var me = await factory.CreateClient().RegisterAsync(name: "Eu Mesmo");
        var other = await factory.CreateClient().RegisterAsync(name: "Outra Pessoa");
        var client = factory.ClientFor(me.AccessToken);
        client.DefaultRequestHeaders.Add("X-User-Id", other.User.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Phone", "+5511900000000");

        var profile = await client.GetFromJsonAsync<UserDto>($"/api/v1/users/me?userId={other.User.Id}&phone=%2B5511900000000");

        Assert.Equal(me.User.Id, profile!.Id);
        Assert.Equal("Eu Mesmo", profile.DisplayName);
    }

    [Fact]
    public async Task The_access_token_carries_only_ids_and_no_personal_data()
    {
        var auth = await factory.CreateClient().RegisterAsync(name: "Nome Secreto Da Pessoa");
        var payload = auth.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));

        Assert.DoesNotContain("Nome Secreto", json);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auth.User.Id.ToString(), json);
    }
}
