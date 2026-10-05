using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

public sealed class GroupLifecycleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creating_a_group_makes_the_creator_the_owner_and_returns_the_invite_code()
    {
        var ana = await factory.NewPersonAsync("Ana");

        var response = await ana.PostAsync("/api/v1/groups", new { name = "  Família   Silva " });
        var group = await response.ReadAsAsync<GroupDetailDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/v1/groups/{group.Id}", response.Headers.Location?.ToString());
        Assert.Equal("Família Silva", group.Name);
        Assert.Equal(GroupRole.Owner, group.MyRole);
        Assert.True(group.InviteEnabled);
        Assert.Matches(new Regex(InviteCodes.DatabasePattern), group.InviteCode!);

        var owner = Assert.Single(group.Members);
        Assert.Equal(group.MyMemberId, owner.Id);
        Assert.Equal("Ana", owner.DisplayName);
        Assert.Equal(GroupRole.Owner, owner.Role);
        Assert.True(owner.HasAccount);
        Assert.True(owner.IsMe);
    }

    [Theory]
    [InlineData("""{"name":"a"}""", "group.name_invalid")]
    [InlineData("""{"name":"   "}""", "validation.failed")]
    [InlineData("""{"name":"Nome​invisível"}""", "group.name_invalid")]
    [InlineData("""{"name":"Um nome de grupo comprido demais para caber no limite"}""", "group.name_invalid")]
    [InlineData("""{}""", "validation.failed")]
    public async Task Creating_a_group_validates_the_name(string json, string code)
    {
        var ana = await factory.NewPersonAsync("Ana");

        var response = await ana.Client.PostAsync("/api/v1/groups", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ReadCodeAsync());
        Assert.Empty(await ana.ListGroupsAsync());
    }

    [Fact]
    public async Task Every_group_route_requires_a_login()
    {
        var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        var responses = new[]
        {
            await anonymous.GetAsync("/api/v1/groups"),
            await anonymous.GetAsync($"/api/v1/groups/{id}"),
            await anonymous.PostAsync("/api/v1/groups", JsonBody("""{"name":"Intruso"}""")),
            await anonymous.PostAsync("/api/v1/groups/join", JsonBody("""{"code":"ABCDEFGH"}""")),
            await anonymous.PostAsync("/api/v1/groups/lookup", JsonBody("""{"code":"ABCDEFGH"}""")),
            await anonymous.PostAsync($"/api/v1/groups/{id}/invite-code", JsonBody("{}")),
            await anonymous.DeleteAsync($"/api/v1/groups/{id}/members/me"),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task The_number_of_groups_per_person_is_limited()
    {
        var limited = factory.WithSettings(("Groups:MaxGroupsPerUser", "2"));
        var ana = await limited.NewPersonAsync("Ana");
        var first = await ana.CreateGroupAsync("Primeiro");
        await ana.CreateGroupAsync("Segundo");

        var third = await ana.PostAsync("/api/v1/groups", new { name = "Terceiro" });
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("group.limit_reached", await third.ReadCodeAsync());

        // Excluir um grupo libera a vaga.
        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync(first.Url(), new { confirmation = "EXCLUIR" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/v1/groups", new { name = "Terceiro" })).StatusCode);
    }

    [Fact]
    public async Task Creating_groups_is_rate_limited_per_person()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:GroupCreatePerHour", "2"));
        var ana = await limited.NewPersonAsync("Ana");
        var beto = await limited.NewPersonAsync("Beto");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await ana.PostAsync("/api/v1/groups", new { name = $"Grupo {i}" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.Created, (await beto.PostAsync("/api/v1/groups", new { name = "Do Beto" })).StatusCode);
    }

    [Fact]
    public async Task Listing_shows_only_my_active_groups_in_alphabetical_order_with_role_and_size()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var zeta = await ana.CreateGroupAsync("Zeta");
        var alfa = await beto.CreateGroupAsync("Alfa");
        var beta = await ana.CreateGroupAsync("beta");
        await beto.CreateGroupAsync("De outra pessoa");
        await ana.JoinAsync(alfa.InviteCode!);
        await beto.JoinAsync(beta.InviteCode!);

        var list = await ana.ListGroupsAsync();

        Assert.Equal(["Alfa", "beta", "Zeta"], list.Select(g => g.Name));
        Assert.Equal([GroupRole.Member, GroupRole.Owner, GroupRole.Owner], list.Select(g => g.MyRole));
        Assert.Equal([2, 2, 1], list.Select(g => g.MemberCount));
        Assert.Contains(list, g => g.Id == zeta.Id);
    }

    [Fact]
    public async Task Listing_leaves_out_groups_I_left_or_that_were_deleted()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var left = await beto.CreateGroupAsync("Saí deste");
        var deleted = await beto.CreateGroupAsync("Excluído");
        var kept = await beto.CreateGroupAsync("Fico");
        foreach (var group in new[] { left, deleted, kept })
        {
            await ana.JoinAsync(group.InviteCode!);
        }

        await ana.DeleteAsync($"/api/v1/groups/{left.Id}/members/me");
        await beto.DeleteAsync(deleted.Url(), new { confirmation = "EXCLUIR" });

        var list = await ana.ListGroupsAsync();

        Assert.Equal(["Fico"], list.Select(g => g.Name));
    }

    [Fact]
    public async Task A_member_sees_the_group_members_and_the_invite_code_ordered_by_power()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var davi = await factory.NewPersonAsync("Davi");
        var group = await ana.GroupWithAsync(beto, carla, davi);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(davi).Id}"), new { role = "admin" });

        var seenByBeto = await beto.GetGroupAsync(group.Id);

        Assert.Equal(["Ana", "Davi", "Beto", "Carla"], seenByBeto.Members.Select(m => m.DisplayName));
        Assert.Equal([GroupRole.Owner, GroupRole.Admin, GroupRole.Member, GroupRole.Member], seenByBeto.Members.Select(m => m.Role));
        Assert.Equal(GroupRole.Member, seenByBeto.MyRole);
        Assert.Equal(group.InviteCode, seenByBeto.InviteCode);
        Assert.Equal(["Beto"], seenByBeto.Members.Where(m => m.IsMe).Select(m => m.DisplayName));
        Assert.Equal(seenByBeto.MyMemberId, seenByBeto.MemberOf(beto).Id);
    }

    [Fact]
    public async Task People_outside_the_group_and_unknown_ids_get_the_same_404()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.CreateGroupAsync();

        var outsider = await intruder.GetAsync(group.Url());
        var unknown = await intruder.GetAsync($"/api/v1/groups/{Guid.NewGuid()}");

        var outsiderProblem = await outsider.ReadProblemAsync();
        var unknownProblem = await unknown.ReadProblemAsync();

        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("group.not_found", outsiderProblem.GetProperty("code").GetString());
        Assert.Equal(outsiderProblem.GetProperty("detail").GetString(), unknownProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_outsider_cannot_do_anything_to_a_group()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var intruder = await factory.NewPersonAsync("Intruso");
        var group = await ana.GroupWithAsync(beto);
        var betoId = group.MemberOf(beto).Id;

        var attempts = new[]
        {
            await intruder.PatchAsync(group.Url(), new { name = "Invadido" }),
            await intruder.PostAsync(group.Url("/invite-code")),
            await intruder.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" }),
            await intruder.PostAsync(group.Url("/members"), new { displayName = "Fantasma" }),
            await intruder.PatchAsync(group.Url($"/members/{betoId}"), new { role = "admin" }),
            await intruder.DeleteAsync(group.Url($"/members/{betoId}")),
            await intruder.DeleteAsync(group.Url("/members/me")),
            await intruder.PostAsync(group.Url("/transfer-ownership"), new { memberId = betoId }),
            await intruder.PutFileAsync(group.Url($"/members/{betoId}/avatar"), TestImages.Jpeg()),
            await intruder.DeleteAsync(group.Url($"/members/{betoId}/avatar")),
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var untouched = await ana.GetGroupAsync(group.Id);
        Assert.Equal(group.Name, untouched.Name);
        Assert.Equal(2, untouched.Members.Count);
    }

    [Fact]
    public async Task Owner_and_admin_can_rename_but_a_plain_member_cannot()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(beto).Id}"), new { role = "admin" });

        var byOwner = await ana.PatchAsync(group.Url(), new { name = "  Novo   Nome " });
        var byAdmin = await beto.PatchAsync(group.Url(), new { name = "Nome do Admin" });
        var byMember = await carla.PatchAsync(group.Url(), new { name = "Nome do Membro" });

        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        Assert.Equal("Novo Nome", (await byOwner.ReadAsAsync<GroupDetailDto>()).Name);
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal("group.forbidden", await byMember.ReadCodeAsync());
        Assert.Equal("Nome do Admin", (await carla.GetGroupAsync(group.Id)).Name);
    }

    [Fact]
    public async Task Renaming_validates_the_name_and_an_empty_patch_changes_nothing()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync("Meu Grupo");

        var invalid = await ana.PatchAsync(group.Url(), new { name = "x" });
        var empty = await ana.PatchAsync(group.Url(), new { });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("group.name_invalid", await invalid.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("Meu Grupo", (await ana.GetGroupAsync(group.Id)).Name);
    }

    [Fact]
    public async Task Turning_the_invite_off_hides_the_code_and_blocks_new_members_until_it_is_turned_back_on()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var off = await ana.PatchAsync(group.Url(), new { inviteEnabled = false });
        var offDetail = await off.ReadAsAsync<GroupDetailDto>();
        var blocked = await beto.JoinRawAsync(group.InviteCode!);

        Assert.False(offDetail.InviteEnabled);
        Assert.Null(offDetail.InviteCode);
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
        Assert.Equal("group.invalid_code", await blocked.ReadCodeAsync());

        var on = await (await ana.PatchAsync(group.Url(), new { inviteEnabled = true })).ReadAsAsync<GroupDetailDto>();
        Assert.Equal(group.InviteCode, on.InviteCode); // a mesma senha volta
        await beto.JoinAsync(group.InviteCode!);
    }

    [Fact]
    public async Task Only_admins_and_the_owner_manage_the_invite()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);

        var toggle = await beto.PatchAsync(group.Url(), new { inviteEnabled = false });
        var regenerate = await beto.PostAsync(group.Url("/invite-code"));

        Assert.Equal(HttpStatusCode.Forbidden, toggle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regenerate.StatusCode);
        Assert.True((await ana.GetGroupAsync(group.Id)).InviteEnabled);
    }

    [Fact]
    public async Task Regenerating_the_invite_code_replaces_it_and_turns_it_on()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.CreateGroupAsync();
        await ana.PatchAsync(group.Url(), new { inviteEnabled = false });

        var response = await ana.PostAsync(group.Url("/invite-code"));
        var updated = await response.ReadAsAsync<GroupDetailDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(group.InviteCode, updated.InviteCode);
        Assert.True(updated.InviteEnabled);
        Assert.Equal("group.invalid_code", await (await beto.JoinRawAsync(group.InviteCode!)).ReadCodeAsync());
        await carla.JoinAsync(updated.InviteCode!);
    }

    [Fact]
    public async Task Deleting_the_group_needs_the_exact_confirmation_and_the_owner()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(beto).Id}"), new { role = "admin" });

        var wrong = await ana.DeleteAsync(group.Url(), new { confirmation = "sim" });
        var missing = await ana.DeleteAsync(group.Url(), new { });
        var byAdmin = await beto.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("group.delete_not_confirmed", await wrong.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);
        Assert.Equal(group.Name, (await ana.GetGroupAsync(group.Id)).Name);
    }

    [Fact]
    public async Task A_deleted_group_disappears_for_everyone_and_its_code_stops_working()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto);

        var response = await ana.DeleteAsync(group.Url(), new { confirmation = "excluir" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(group.Url())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync(group.Url())).StatusCode);
        Assert.Empty(await beto.ListGroupsAsync());
        Assert.Equal("group.invalid_code", await (await carla.JoinRawAsync(group.InviteCode!)).ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await ana.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" })).StatusCode);
    }

    [Fact]
    public async Task The_group_row_survives_a_logical_delete()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        await ana.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" });

        var stored = await factory.WithDbAsync(db => db.Groups.AsNoTracking().SingleAsync(g => g.Id == group.Id));

        Assert.True(stored.IsDeleted);
        Assert.False(stored.InviteEnabled);
    }

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
