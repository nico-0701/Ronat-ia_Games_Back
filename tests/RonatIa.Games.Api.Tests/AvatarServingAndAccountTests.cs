using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests;

public sealed class AvatarServingAndAccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(AuthResponse Auth, string PhotoUrl)> UserWithPhotoAsync()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var response = await factory.ClientFor(auth.AccessToken).PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg(300, 300)));
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        return (auth, user!.Avatar.Url!);
    }

    [Fact]
    public async Task The_photo_is_served_publicly_with_immutable_caching_and_defensive_headers()
    {
        var (_, url) = await UserWithPhotoAsync();

        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("cross-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task A_conditional_request_gets_304_and_an_unknown_photo_gets_404()
    {
        var (_, url) = await UserWithPhotoAsync();
        var client = factory.CreateClient();
        var etag = (await client.GetAsync(url)).Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await client.SendAsync(request);
        var missing = await client.GetAsync($"/api/v1/avatars/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("avatar.not_found", await missing.ReadCodeAsync());
    }

    [Fact]
    public async Task Delete_account_requires_the_exact_confirmation_and_a_login()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var client = factory.ClientFor(auth.AccessToken);

        var wrong = await client.SendAsync(Delete("""{"confirmation":"sim"}"""));
        var missing = await client.SendAsync(Delete("{}"));
        var anonymous = await factory.CreateClient().SendAsync(Delete("""{"confirmation":"EXCLUIR"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("user.delete_not_confirmed", await wrong.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_anonymizes_it_kills_the_sessions_removes_the_photo_and_frees_the_phone()
    {
        var phone = TestPhones.Next();
        var registered = await factory.CreateClient().RegisterAsync(phone, "Quem Vai Embora");
        var client = factory.ClientFor(registered.AccessToken);
        var upload = await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg()));
        var photoUrl = (await upload.Content.ReadFromJsonAsync<UserDto>())!.Avatar.Url!;
        var otherDevice = await factory.CreateClient().LoginAsync(phone);

        var response = await client.SendAsync(Delete("""{"confirmation":"excluir"}"""));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(otherDevice.AccessToken).GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().RefreshRawAsync(registered.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(photoUrl)).StatusCode);

        var row = await factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == registered.User.Id));
        Assert.Equal("Jogador removido", row.DisplayName);
        Assert.Equal("deleted", await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT status AS "Value" FROM app.users WHERE id = {registered.User.Id}""").SingleAsync()));
        Assert.NotNull(row.DeletedAt);
        Assert.Equal("0000", row.PhoneLast4);

        // O número volta a estar livre: dá para entrar de novo como uma conta nova.
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().LoginRawAsync(phone)).StatusCode);
        var again = await factory.CreateClient().RegisterAsync(phone, "Voltei");
        Assert.NotEqual(registered.User.Id, again.User.Id);
    }

    private static HttpRequestMessage Delete(string json) => new(HttpMethod.Delete, "/api/v1/users/me")
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}
