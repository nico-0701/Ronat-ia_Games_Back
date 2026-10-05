using System.Net;
using System.Net.Http.Json;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Users;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

/// <summary>Direito de acesso e portabilidade (LGPD): a pessoa baixa uma cópia dos próprios dados, e só dos dela.</summary>
public sealed class PrivacyExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ExportUrl = "/api/v1/users/me/export";

    private static async Task<PersonalDataExportDto> ExportOfAsync(Person person)
    {
        var response = await person.GetAsync(ExportUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<PersonalDataExportDto>();
    }

    /// <summary>Uma partida "solo" de meta 1: quem pontua primeiro vence e os demais ficam em segundo.</summary>
    private static async Task<Guid> PlaySoloAsync(Person host, Guid groupId, Person winner, params Person[] others)
    {
        var session = await host.CreateSessionAsync(groupId, "solo", new { target = 1 });
        foreach (var person in others.Where(p => p != host))
        {
            await person.PostOkAsync(session.SessionUrl("/join"));
        }

        await host.PostOkAsync(session.SessionUrl("/start"));
        await winner.ActOkAsync(session.Id, "add", new { n = 1 });
        return session.Id;
    }

    [Fact]
    public async Task A_new_account_exports_its_profile_and_first_device_and_nothing_else()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ana = await factory.NewPersonAsync("Ana");

        var export = await ExportOfAsync(ana);

        Assert.InRange(export.ExportedAt, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(ana.UserId, export.Profile.Id);
        Assert.Equal("Ana", export.Profile.DisplayName);
        Assert.Equal(ana.Auth.User.PhoneLast4, export.Profile.PhoneLast4);
        Assert.Equal(4, export.Profile.PhoneLast4.Length);
        Assert.NotNull(export.Profile.TermsAcceptedAt);
        Assert.False(string.IsNullOrWhiteSpace(export.Profile.TermsVersion));
        var login = Assert.Single(export.Logins);
        Assert.Null(login.RevokedAt);
        Assert.Empty(export.Memberships);
        Assert.Empty(export.Results);
    }

    [Fact]
    public async Task The_export_brings_the_groups_and_the_results_of_the_person()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var sessionId = await PlaySoloAsync(ana, group.Id, winner: ana, beto);

        var anaExport = await ExportOfAsync(ana);
        var betoExport = await ExportOfAsync(beto);

        var membership = Assert.Single(anaExport.Memberships);
        Assert.Equal(group.Id, membership.GroupId);
        Assert.Equal(group.Name, membership.GroupName);
        Assert.Equal(GroupRole.Owner, membership.Role);
        Assert.Equal(MemberStatus.Active, membership.Status);
        Assert.Null(membership.LeftAt);
        Assert.Equal(group.MemberOf(ana).Id, membership.MemberId);

        var anaResult = Assert.Single(anaExport.Results);
        Assert.Equal(sessionId, anaResult.SessionId);
        Assert.Equal(group.Id, anaResult.GroupId);
        Assert.Equal("solo", anaResult.GameId);
        Assert.True(anaResult.IsWinner);
        Assert.Equal(1, anaResult.Rank);

        var betoResult = Assert.Single(betoExport.Results);
        Assert.Equal(sessionId, betoResult.SessionId);
        Assert.False(betoResult.IsWinner);
        Assert.Equal(2, betoResult.Rank);
        Assert.Equal(GroupRole.Member, Assert.Single(betoExport.Memberships).Role);
    }

    [Fact]
    public async Task The_export_keeps_the_groups_the_person_left_and_the_devices_that_were_closed()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        Assert.Equal(HttpStatusCode.NoContent, (await beto.DeleteAsync($"/api/v1/groups/{group.Id}/members/me")).StatusCode);

        var client = factory.CreateClient();
        var second = await client.PostAsJsonAsync("/api/v1/auth/login", new { phone = beto.Phone, deviceName = "Segundo aparelho" });
        var secondToken = (await second.ReadAsAsync<AuthResponse>()).AccessToken;
        Assert.Equal(HttpStatusCode.NoContent, (await beto.PostAsync("/api/v1/auth/logout")).StatusCode);

        var export = await ExportOfAsync(new Person { Auth = beto.Auth, Client = factory.ClientFor(secondToken), Phone = beto.Phone });

        var membership = Assert.Single(export.Memberships);
        Assert.Equal(MemberStatus.Left, membership.Status);
        Assert.NotNull(membership.LeftAt);
        Assert.Equal(2, export.Logins.Count);
        Assert.Single(export.Logins, login => login.RevokedAt is not null && login.RevokedReason == "logout");
        Assert.Single(export.Logins, login => login.RevokedAt is null && login.DeviceLabel == "Segundo aparelho");
    }

    [Fact]
    public async Task The_export_never_carries_other_peoples_data_nor_the_phone_nor_secrets()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto Revelador");
        var group = await ana.GroupWithAsync(beto);
        await PlaySoloAsync(ana, group.Id, winner: ana, beto);

        var response = await ana.GetAsync(ExportUrl);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Beto", body);
        Assert.DoesNotContain(beto.UserId.ToString(), body);
        Assert.DoesNotContain(ana.Phone!, body);
        Assert.DoesNotContain(ana.Phone!.TrimStart('+'), body);
        Assert.DoesNotContain(group.InviteCode!, body);
        Assert.DoesNotContain(ana.Auth.RefreshToken, body);
        Assert.DoesNotContain(ana.Auth.AccessToken, body);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pepper", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_export_requires_a_login_and_is_never_cached()
    {
        var anonymous = await factory.CreateClient().GetAsync(ExportUrl);
        var ana = await factory.NewPersonAsync("Ana");

        var response = await ana.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true, "a cópia dos dados pessoais não pode ser guardada em cache");
    }

    [Fact]
    public async Task The_export_is_limited_per_person()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:ExportPerHour", "2"));
        var ana = await limited.NewPersonAsync("Ana");
        var beto = await limited.NewPersonAsync("Beto");

        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync(ExportUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync(ExportUrl)).StatusCode);
        var refused = await ana.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("rate_limit.exceeded", await refused.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await beto.GetAsync(ExportUrl)).StatusCode); // cada pessoa tem a própria cota
    }
}
