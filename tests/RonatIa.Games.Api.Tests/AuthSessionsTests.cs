using System.Net;
using System.Net.Http.Json;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests;

public sealed class AuthSessionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<AuthResponse> LoginFromDeviceAsync(HttpClient client, string phone, string device)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { phone, deviceName = device });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    [Fact]
    public async Task Sessions_lists_the_connected_devices_and_marks_the_current_one()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        _ = await client.RegisterAsync(phone);
        var tablet = await LoginFromDeviceAsync(client, phone, "Tablet da sala");
        var phoneDevice = await LoginFromDeviceAsync(client, phone, "Pixel 7");

        var sessions = await factory.ClientFor(phoneDevice.AccessToken).GetFromJsonAsync<List<SessionDto>>("/api/v1/auth/sessions");

        Assert.Equal(3, sessions!.Count);
        Assert.Single(sessions, session => session.IsCurrent);
        Assert.Equal("Pixel 7", sessions.Single(session => session.IsCurrent).DeviceLabel);
        Assert.Contains(sessions, session => session.DeviceLabel == "Tablet da sala");
        Assert.NotNull(tablet.AccessToken);
    }

    [Fact]
    public async Task Revoking_another_device_invalidates_its_tokens_immediately_and_keeps_the_current_one()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var current = await client.RegisterAsync(phone);
        var other = await LoginFromDeviceAsync(client, phone, "Aparelho perdido");
        var currentClient = factory.ClientFor(current.AccessToken);
        var otherClient = factory.ClientFor(other.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync("/api/v1/users/me")).StatusCode); // aquece o cache

        var sessions = await currentClient.GetFromJsonAsync<List<SessionDto>>("/api/v1/auth/sessions");
        var lost = sessions!.Single(session => session.DeviceLabel == "Aparelho perdido");
        var revoke = await currentClient.DeleteAsync($"/api/v1/auth/sessions/{lost.Id}");

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshRawAsync(other.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await currentClient.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_all_ends_every_session_of_the_account()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var first = await client.RegisterAsync(phone);
        var second = await LoginFromDeviceAsync(client, phone, "Segundo aparelho");

        var response = await factory.ClientFor(first.AccessToken).PostAsync("/api/v1/auth/logout-all", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(first.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(second.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshRawAsync(first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshRawAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logging_out_does_not_affect_other_devices()
    {
        var client = factory.CreateClient();
        var phone = TestPhones.Next();
        var first = await client.RegisterAsync(phone);
        var second = await LoginFromDeviceAsync(client, phone, "Outro aparelho");

        await factory.ClientFor(first.AccessToken).PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(second.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Nobody_can_revoke_a_session_that_belongs_to_someone_else()
    {
        var victim = await factory.CreateClient().RegisterAsync(name: "Dono da sessão");
        var attacker = await factory.CreateClient().RegisterAsync(name: "Curioso");
        var victimSessions = await factory.ClientFor(victim.AccessToken).GetFromJsonAsync<List<SessionDto>>("/api/v1/auth/sessions");

        var response = await factory.ClientFor(attacker.AccessToken).DeleteAsync($"/api/v1/auth/sessions/{victimSessions![0].Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("auth.session_not_found", await response.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(victim.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Session_list_requires_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/auth/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task When_the_device_limit_is_exceeded_the_least_recently_used_session_is_disconnected()
    {
        var limited = factory.WithSettings(("Auth:MaxSessionsPerUser", "2"));
        var client = limited.CreateClient();
        var phone = TestPhones.Next();
        var oldest = await client.RegisterAsync(phone);
        var middle = await LoginFromDeviceAsync(client, phone, "Segundo");
        var newest = await LoginFromDeviceAsync(client, phone, "Terceiro");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshRawAsync(oldest.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.RefreshRawAsync(middle.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.RefreshRawAsync(newest.RefreshToken)).StatusCode);
    }
}
