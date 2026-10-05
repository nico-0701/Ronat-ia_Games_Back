using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;

namespace RonatIa.Games.Api.Tests;

public sealed class UserProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Avatar_presets_are_public_and_listed_with_a_default()
    {
        var presets = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/avatars/presets");

        Assert.Equal("preset-1", presets.GetProperty("default").GetString());
        var keys = presets.GetProperty("keys").EnumerateArray().Select(key => key.GetString()).ToList();
        Assert.Equal(6, keys.Count);
        Assert.Equal("preset-1", keys[0]);
        Assert.Equal("preset-6", keys[^1]);
    }

    [Fact]
    public async Task Patch_changes_the_name_and_normalizes_it()
    {
        var auth = await factory.CreateClient().RegisterAsync(name: "Nome Antigo");
        var client = factory.ClientFor(auth.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { displayName = "  Nome   Novo  " });
        var user = await response.Content.ReadFromJsonAsync<UserDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Nome Novo", user!.DisplayName);
        Assert.Equal("Nome Novo", (await client.GetFromJsonAsync<UserDto>("/api/v1/users/me"))!.DisplayName);
    }

    [Fact]
    public async Task Patch_chooses_another_avatar_preset()
    {
        var auth = await factory.CreateClient().RegisterAsync(preset: "preset-1");

        var response = await factory.ClientFor(auth.AccessToken).PatchAsJsonAsync("/api/v1/users/me", new { avatarPreset = "preset-5" });
        var user = await response.Content.ReadFromJsonAsync<UserDto>();

        Assert.Equal("preset", user!.Avatar.Kind);
        Assert.Equal("preset-5", user.Avatar.Preset);
    }

    [Fact]
    public async Task Patch_with_nothing_to_change_is_a_no_op()
    {
        var auth = await factory.CreateClient().RegisterAsync(name: "Sem Mudança", preset: "preset-3");

        var response = await factory.ClientFor(auth.AccessToken).PatchAsJsonAsync("/api/v1/users/me", new { });
        var user = await response.Content.ReadFromJsonAsync<UserDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Sem Mudança", user!.DisplayName);
        Assert.Equal("preset-3", user.Avatar.Preset);
    }

    [Theory]
    [InlineData("""{"displayName":"a"}""", "user.display_name_invalid")]
    [InlineData("""{"displayName":"Nome​invisível"}""", "user.display_name_invalid")]
    [InlineData("""{"avatarPreset":"preset-99"}""", "avatar.unknown_preset")]
    public async Task Patch_rejects_invalid_values_and_changes_nothing(string json, string code)
    {
        var auth = await factory.CreateClient().RegisterAsync(name: "Continua Igual", preset: "preset-2");
        var client = factory.ClientFor(auth.AccessToken);

        var response = await client.PatchAsync("/api/v1/users/me", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ReadCodeAsync());
        Assert.Equal("Continua Igual", me!.DisplayName);
        Assert.Equal("preset-2", me.Avatar.Preset);
    }

    [Fact]
    public async Task Patch_requires_authentication()
    {
        var response = await factory.CreateClient().PatchAsJsonAsync("/api/v1/users/me", new { displayName = "Intruso" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Each_person_only_changes_their_own_profile()
    {
        var first = await factory.CreateClient().RegisterAsync(name: "Primeira Pessoa");
        var second = await factory.CreateClient().RegisterAsync(name: "Segunda Pessoa");

        await factory.ClientFor(first.AccessToken).PatchAsJsonAsync("/api/v1/users/me", new { displayName = "Renomeada" });

        Assert.Equal("Segunda Pessoa", (await factory.ClientFor(second.AccessToken).GetFromJsonAsync<UserDto>("/api/v1/users/me"))!.DisplayName);
    }

    [Fact]
    public async Task Choosing_a_preset_discards_the_uploaded_photo()
    {
        var auth = await factory.CreateClient().RegisterAsync();
        var client = factory.ClientFor(auth.AccessToken);
        await client.PutAsync("/api/v1/users/me/avatar", TestImages.Form(TestImages.Jpeg()));
        Assert.Equal(1, await CountPhotosAsync(auth.User.Id));

        var response = await client.PatchAsJsonAsync("/api/v1/users/me", new { avatarPreset = "preset-4" });
        var user = await response.Content.ReadFromJsonAsync<UserDto>();

        Assert.Equal("preset", user!.Avatar.Kind);
        Assert.Equal(0, await CountPhotosAsync(auth.User.Id));
    }

    private Task<int> CountPhotosAsync(Guid userId) =>
        factory.WithDbAsync(db => db.Avatars.CountAsync(avatar => avatar.UploadedByUserId == userId));
}
