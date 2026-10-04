using System.Net;
using Microsoft.Extensions.Time.Testing;
using RonatIa.Games.Api.Tests.Infrastructure;

namespace RonatIa.Games.Api.Tests;

/// <summary>Rotação do refresh token, tolerância a rede instável e detecção de reutilização.</summary>
public sealed class AuthRefreshTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Refresh_returns_a_new_token_pair_and_the_session_stays_the_same()
    {
        var client = factory.CreateClient();
        var first = await client.RegisterAsync();

        var second = await client.RefreshAsync(first.RefreshToken);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.False(second.IsNewUser);
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(second.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task A_replaced_token_replayed_after_the_grace_window_revokes_the_whole_session()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var client = host.CreateClient();
        var first = await client.RegisterAsync();
        var second = await client.RefreshAsync(first.RefreshToken);
        time.Advance(TimeSpan.FromSeconds(30));

        var replay = await client.RefreshRawAsync(first.RefreshToken);
        var legitimate = await client.RefreshRawAsync(second.RefreshToken);
        var me = await host.ClientFor(second.AccessToken).GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("auth.invalid_refresh_token", await replay.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Retrying_with_the_old_token_inside_the_grace_window_works_and_returns_a_valid_token()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var client = host.CreateClient();
        var first = await client.RegisterAsync();
        _ = await client.RefreshAsync(first.RefreshToken); // a resposta se perdeu no caminho
        time.Advance(TimeSpan.FromSeconds(5));

        var retry = await client.RefreshAsync(first.RefreshToken);
        var next = await client.RefreshRawAsync(retry.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Several_retries_inside_the_grace_window_keep_working()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var client = host.CreateClient();
        var first = await client.RegisterAsync();
        _ = await client.RefreshAsync(first.RefreshToken);

        time.Advance(TimeSpan.FromSeconds(5));
        _ = await client.RefreshAsync(first.RefreshToken);
        time.Advance(TimeSpan.FromSeconds(5));
        var third = await client.RefreshAsync(first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, (await client.RefreshRawAsync(third.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_refreshes_with_the_same_token_never_log_the_user_out_by_accident()
    {
        var client = factory.CreateClient();
        var first = await client.RegisterAsync();

        var responses = await Task.WhenAll(
            factory.CreateClient().RefreshRawAsync(first.RefreshToken),
            factory.CreateClient().RefreshRawAsync(first.RefreshToken),
            factory.CreateClient().RefreshRawAsync(first.RefreshToken));

        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Esperado 200 ou 409, veio {(int)response.StatusCode}"));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refresh_token_expires_after_its_lifetime()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = factory.WithFakeTime(time);
        var client = host.CreateClient();
        var auth = await client.RegisterAsync();

        time.Advance(TimeSpan.FromDays(89));
        var stillValid = await client.RefreshAsync(auth.RefreshToken); // renova (janela deslizante)
        time.Advance(TimeSpan.FromDays(91));
        var expired = await client.RefreshRawAsync(stillValid.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("auth.invalid_refresh_token", await expired.ReadCodeAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("este-nao-e-um-refresh-token-valido-de-jeito-nenhum-1234567890")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task Garbage_refresh_tokens_are_rejected(string token)
    {
        var response = await factory.CreateClient().RefreshRawAsync(token);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_but_well_formed_refresh_token_is_rejected()
    {
        var response = await factory.CreateClient().RefreshRawAsync(new string('A', 43));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_refresh_token", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Logout_invalidates_the_refresh_token_and_the_access_token_immediately()
    {
        var client = factory.CreateClient();
        var auth = await client.RegisterAsync();
        var authed = factory.ClientFor(auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/api/v1/users/me")).StatusCode); // aquece o cache da sessão

        var logout = await authed.PostAsync("/api/v1/auth/logout", null);
        var me = await authed.GetAsync("/api/v1/users/me");
        var refresh = await client.RefreshRawAsync(auth.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
