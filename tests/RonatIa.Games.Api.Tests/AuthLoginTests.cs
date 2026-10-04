using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Abstractions;

namespace RonatIa.Games.Api.Tests;

public sealed class AuthLoginTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_with_an_existing_phone_starts_a_new_session()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var registered = await client.RegisterAsync(phone, "Quem Já Tem Conta");

        var login = await client.LoginAsync(phone);

        Assert.False(login.IsNewUser);
        Assert.Equal(registered.User.Id, login.User.Id);
        Assert.NotEqual(registered.RefreshToken, login.RefreshToken);
        Assert.NotEqual(registered.AccessToken, login.AccessToken);

        var sessions = await factory.ClientFor(login.AccessToken).GetFromJsonAsync<JsonElement>("/api/v1/auth/sessions");
        Assert.Equal(2, sessions.GetArrayLength());
    }

    [Fact]
    public async Task Login_with_an_unknown_phone_asks_the_client_to_register()
    {
        var response = await factory.CreateClient().LoginRawAsync(TestPhones.Next());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("auth.user_not_found", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Login_accepts_any_format_of_the_same_number()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var national = phone[3..];
        var registered = await client.RegisterAsync(phone);

        var formats = new[]
        {
            phone,
            $"+55 {national[..2]} {national[2..7]}-{national[7..]}",
            $"({national[..2]}) {national[2..7]}-{national[7..]}",
            national,
            $"0{national}",
        };

        foreach (var format in formats)
        {
            var login = await client.LoginAsync(format);
            Assert.Equal(registered.User.Id, login.User.Id);
        }
    }

    [Theory]
    [InlineData("123")]
    [InlineData("não é telefone")]
    public async Task Login_rejects_invalid_phones(string phone)
    {
        var response = await factory.CreateClient().LoginRawAsync(phone);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.invalid_phone", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Login_without_a_phone_is_a_validation_error()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task A_suspended_account_cannot_log_in_and_its_tokens_stop_working()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var auth = await client.RegisterAsync(phone);

        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE app.users SET status = 'suspended' WHERE id = {auth.User.Id}"));

        var login = await client.LoginRawAsync(phone);
        var me = await factory.ClientFor(auth.AccessToken).GetAsync("/api/v1/users/me");
        var refresh = await client.RefreshRawAsync(auth.RefreshToken);

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("auth.account_suspended", await login.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task A_deleted_account_looks_like_it_does_not_exist()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var auth = await client.RegisterAsync(phone);

        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE app.users SET status = 'deleted' WHERE id = {auth.User.Id}"));

        var login = await client.LoginRawAsync(phone);

        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
    }

    [Fact]
    public async Task When_captcha_is_enabled_login_and_registration_require_a_valid_token()
    {
        var phone = TestPhones.Next();
        await factory.CreateClient().RegisterAsync(phone);

        var withCaptcha = factory
            .WithSettings(("Turnstile:SecretKey", "segredo-de-teste"), ("Turnstile:SiteKey", "chave-publica"))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICaptchaVerifier>();
                services.AddSingleton<ICaptchaVerifier, FakeCaptchaVerifier>();
            }));
        var client = withCaptcha.CreateClient();

        var missing = await client.LoginRawAsync(phone);
        var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new { phone, captchaToken = "ruim" });
        var valid = await client.PostAsJsonAsync("/api/v1/auth/login", new { phone, captchaToken = FakeCaptchaVerifier.ValidToken });
        var registerWithout = await client.PostAsJsonAsync("/api/v1/auth/register", new { phone = TestPhones.Next(), displayName = "Sem Captcha", acceptTerms = true });
        var meta = await client.GetFromJsonAsync<JsonElement>("/api/v1/meta");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("auth.captcha_failed", await missing.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, registerWithout.StatusCode);
        Assert.True(meta.GetProperty("auth").GetProperty("captchaRequired").GetBoolean());
        Assert.Equal("chave-publica", meta.GetProperty("auth").GetProperty("captchaSiteKey").GetString());
    }

    [Fact]
    public async Task Meta_does_not_advertise_a_captcha_when_it_is_disabled()
    {
        var meta = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/meta");

        Assert.True(meta.GetProperty("auth").GetProperty("registrationOpen").GetBoolean());
        Assert.False(meta.GetProperty("auth").GetProperty("captchaRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, meta.GetProperty("auth").GetProperty("captchaSiteKey").ValueKind);
    }

    private sealed class FakeCaptchaVerifier : ICaptchaVerifier
    {
        public const string ValidToken = "token-valido";

        public bool IsEnabled => true;

        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken) =>
            Task.FromResult(token == ValidToken);
    }
}
