using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests;

public sealed class AuthRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Register_creates_the_account_and_returns_working_tokens()
    {
        var phone = TestPhones.Next();

        var auth = await factory.CreateClient().RegisterAsync(phone, "Zé Milton", "preset-3");

        Assert.True(auth.IsNewUser);
        Assert.Equal("Zé Milton", auth.User.DisplayName);
        Assert.Equal("preset", auth.User.Avatar.Kind);
        Assert.Equal("preset-3", auth.User.Avatar.Preset);
        Assert.Equal(phone[^4..], auth.User.PhoneLast4);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.Equal(RefreshTokens.TokenLength, auth.RefreshToken.Length);
        Assert.True(auth.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(auth.RefreshTokenExpiresAt > auth.AccessTokenExpiresAt);

        var me = await factory.ClientFor(auth.AccessToken).GetFromJsonAsync<UserDto>("/api/v1/users/me");
        Assert.Equal(auth.User.Id, me!.Id);
        Assert.Equal("Zé Milton", me.DisplayName);
    }

    [Fact]
    public async Task Register_without_an_avatar_uses_the_default_preset()
    {
        var auth = await factory.CreateClient().RegisterAsync();

        Assert.Equal("preset-1", auth.User.Avatar.Preset);
    }

    [Fact]
    public async Task Register_normalizes_the_display_name()
    {
        var auth = await factory.CreateClient().RegisterAsync(name: "  Tia    Jana  ");

        Assert.Equal("Tia Jana", auth.User.DisplayName);
    }

    [Fact]
    public async Task Register_twice_with_the_same_phone_is_a_conflict()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        await client.RegisterAsync(phone);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { phone, displayName = "Outra Pessoa", acceptTerms = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("auth.phone_taken", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Different_formats_of_the_same_number_are_the_same_account()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next(); // +5511 9 XXXX XXXX
        var national = phone[3..];
        var masked = $"({national[..2]}) {national[2..7]}-{national[7..]}";
        await client.RegisterAsync(masked);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { phone, displayName = "Duplicada", acceptTerms = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_requires_accepting_the_terms()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { phone = TestPhones.Next(), displayName = "Sem Termos", acceptTerms = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.terms_not_accepted", await response.ReadCodeAsync());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("+55 11 1234-5678 ramal 9")]
    [InlineData("+55 00 98888-7777")]
    public async Task Register_rejects_invalid_phones(string phone)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { phone, displayName = "Telefone Ruim", acceptTerms = true });
        var problem = await response.ReadProblemAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.invalid_phone", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("phone", out _));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("Nome​com invisível")]
    public async Task Register_rejects_invalid_names(string name)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { phone = TestPhones.Next(), displayName = name, acceptTerms = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_an_unknown_avatar_preset()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { phone = TestPhones.Next(), displayName = "Avatar Falso", avatarPreset = "preset-99", acceptTerms = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("avatar.unknown_preset", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Register_requires_a_body()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.failed", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Simultaneous_registrations_of_the_same_phone_create_exactly_one_account()
    {
        var phone = TestPhones.Next();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new { phone, displayName = $"Pessoa {i + 10}", acceptTerms = true })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task The_phone_number_is_never_stored_in_clear()
    {
        var phone = TestPhones.Next();
        var auth = await factory.CreateClient().RegisterAsync(phone, "Privacidade");

        var row = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT row_to_json(u)::text AS "Value" FROM app.users u WHERE u.id = {auth.User.Id}""")
            .SingleAsync());
        var hashLength = await factory.WithDbAsync(db => db.Database
            .SqlQuery<int>($"""SELECT octet_length(phone_hash) AS "Value" FROM app.users WHERE id = {auth.User.Id}""")
            .SingleAsync());

        Assert.DoesNotContain(phone[1..], row);
        Assert.DoesNotContain(phone[^8..], row);
        Assert.Contains(phone[^4..], row);
        Assert.Equal(32, hashLength);
    }

    [Fact]
    public async Task Registration_can_be_closed_without_blocking_existing_accounts()
    {
        var phone = TestPhones.Next();
        await factory.CreateClient().RegisterAsync(phone);
        var closed = factory.WithSettings(("Registration:Mode", "Closed"));

        var registration = await closed.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new { phone = TestPhones.Next(), displayName = "Chegou Tarde", acceptTerms = true });
        var login = await closed.CreateClient().LoginRawAsync(phone);
        var meta = await closed.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/meta");

        Assert.Equal(HttpStatusCode.Forbidden, registration.StatusCode);
        Assert.Equal("auth.registration_closed", await registration.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(meta.GetProperty("auth").GetProperty("registrationOpen").GetBoolean());
    }
}
