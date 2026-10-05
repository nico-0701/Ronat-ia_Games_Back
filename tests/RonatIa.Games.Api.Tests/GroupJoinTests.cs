using System.Net;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

public sealed class GroupJoinTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_code_lets_someone_join_as_a_plain_member()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync("Amigos");

        var joined = await beto.JoinAsync(group.InviteCode!);

        Assert.Equal(group.Id, joined.Id);
        Assert.Equal(GroupRole.Member, joined.MyRole);
        Assert.Equal(["Ana", "Beto"], joined.Members.Select(m => m.DisplayName));
        Assert.Equal(joined.MyMemberId, joined.MemberOf(beto).Id);
        Assert.Equal(2, (await ana.ListGroupsAsync()).Single().MemberCount);
        Assert.Equal(["Amigos"], (await beto.ListGroupsAsync()).Select(g => g.Name));
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("hyphen")]
    [InlineData("spaces")]
    public async Task The_code_is_forgiving_about_how_it_is_typed(string style)
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();
        var code = group.InviteCode!;
        var typed = style switch
        {
            "lower" => code.ToLowerInvariant(),
            "hyphen" => $"{code[..4]}-{code[4..]}",
            _ => $"  {code[..4]} {code[4..]}  ",
        };

        var joined = await beto.JoinAsync(typed);

        Assert.Equal(group.Id, joined.Id);
    }

    [Fact]
    public async Task Joining_twice_is_harmless()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var first = await beto.JoinAsync(group.InviteCode!);
        var second = await beto.JoinAsync(group.InviteCode!);

        Assert.Equal(first.MyMemberId, second.MyMemberId);
        Assert.Equal(2, second.Members.Count);
    }

    [Fact]
    public async Task The_owner_joining_with_their_own_code_stays_the_owner()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var again = await ana.JoinAsync(group.InviteCode!);

        Assert.Equal(GroupRole.Owner, again.MyRole);
        Assert.Single(again.Members);
    }

    [Fact]
    public async Task Wrong_disabled_and_deleted_codes_all_answer_the_same_way()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var disabled = await ana.CreateGroupAsync("Desligado");
        var deleted = await ana.CreateGroupAsync("Excluído");
        await ana.PatchAsync(disabled.Url(), new { inviteEnabled = false });
        await ana.DeleteAsync(deleted.Url(), new { confirmation = "EXCLUIR" });

        var attempts = new[]
        {
            "123",                         // curto demais
            "ABCDEFGH",                    // formato válido, mas não existe
            "abcd-efgi",                   // I não existe no alfabeto
            disabled.InviteCode!,
            deleted.InviteCode!,
        };

        var details = new HashSet<string?>();
        foreach (var code in attempts)
        {
            var response = await beto.JoinRawAsync(code);
            var problem = await response.ReadProblemAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("group.invalid_code", problem.GetProperty("code").GetString());
            details.Add(problem.GetProperty("detail").GetString());
        }

        Assert.Single(details); // nada revela se o código existe, está desligado ou foi excluído

        // Um texto absurdamente longo nem chega à busca: é um pedido malformado.
        var absurd = await beto.JoinRawAsync(new string('A', 500));
        Assert.Equal(HttpStatusCode.BadRequest, absurd.StatusCode);
        Assert.Equal("validation.failed", await absurd.ReadCodeAsync());
        Assert.Empty(await beto.ListGroupsAsync());
    }

    [Fact]
    public async Task A_missing_code_is_a_validation_error()
    {
        var beto = await factory.NewPersonAsync("Beto");

        var response = await beto.PostAsync("/api/v1/groups/join", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_shows_what_the_code_reveals_without_joining()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync("Família Silva");
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa", "preset-3");
        var tio = await ana.AddProfileAsync(group.Id, "Tio Zé");
        await ana.DeleteAsync(group.Url($"/members/{tio.Id}")); // removido: não pode ser assumido

        var response = await beto.PostAsync("/api/v1/groups/lookup", new { code = group.InviteCode!.ToLowerInvariant() });
        var preview = await response.ReadAsAsync<GroupPreviewDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Família Silva", preview.Name);
        Assert.Equal(2, preview.MemberCount); // Ana + Vovó Rosa
        Assert.False(preview.AlreadyMember);
        var claimable = Assert.Single(preview.ClaimableMembers);
        Assert.Equal(vovo.Id, claimable.Id);
        Assert.Equal("Vovó Rosa", claimable.DisplayName);
        Assert.Equal("preset-3", claimable.Avatar.Preset);
        Assert.Empty(await beto.ListGroupsAsync()); // consultar não entra no grupo
    }

    [Fact]
    public async Task Lookup_tells_members_they_are_already_in()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var preview = await (await ana.PostAsync("/api/v1/groups/lookup", new { code = group.InviteCode })).ReadAsAsync<GroupPreviewDto>();

        Assert.True(preview.AlreadyMember);
    }

    [Fact]
    public async Task Lookup_with_a_bad_code_is_the_same_404()
    {
        var beto = await factory.NewPersonAsync("Beto");

        var response = await beto.PostAsync("/api/v1/groups/lookup", new { code = "ZZZZZZZZ" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("group.invalid_code", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Leaving_removes_the_group_from_my_view_and_rejoining_restores_the_same_member()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var betoMember = group.MemberOf(beto).Id;
        await ana.PatchAsync(group.Url($"/members/{betoMember}"), new { role = "admin" });

        var left = await beto.DeleteAsync(group.Url("/members/me"));

        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync(group.Url())).StatusCode);
        Assert.Empty(await beto.ListGroupsAsync());
        Assert.Single((await ana.GetGroupAsync(group.Id)).Members);

        var back = await beto.JoinAsync(group.InviteCode!);
        Assert.Equal(betoMember, back.MyMemberId);       // o mesmo vínculo, com o mesmo histórico
        Assert.Equal(GroupRole.Member, back.MyRole);      // administrador não volta como administrador
        Assert.Equal(2, back.Members.Count);
    }

    [Fact]
    public async Task Leaving_twice_or_without_being_a_member_is_a_404()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);

        await beto.DeleteAsync(group.Url("/members/me"));

        Assert.Equal(HttpStatusCode.NotFound, (await beto.DeleteAsync(group.Url("/members/me"))).StatusCode);
    }

    [Fact]
    public async Task The_owner_cannot_leave()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var response = await ana.DeleteAsync(group.Url("/members/me"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("group.owner_cannot_leave", await response.ReadCodeAsync());
        Assert.Equal(GroupRole.Owner, (await ana.GetGroupAsync(group.Id)).MyRole);
    }

    [Fact]
    public async Task A_removed_member_can_come_back_with_the_code_unless_it_was_changed()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.DeleteAsync(group.Url($"/members/{group.MemberOf(beto).Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync(group.Url())).StatusCode);

        // Para barrar de vez, o dono gera outra senha.
        var renewed = await (await ana.PostAsync(group.Url("/invite-code"))).ReadAsAsync<GroupDetailDto>();
        Assert.Equal("group.invalid_code", await (await beto.JoinRawAsync(group.InviteCode!)).ReadCodeAsync());

        // Quem ainda tiver uma senha válida pode voltar.
        var back = await beto.JoinAsync(renewed.InviteCode!);
        Assert.Equal(GroupRole.Member, back.MyRole);
    }

    [Fact]
    public async Task A_full_group_refuses_new_members_and_new_profiles()
    {
        var small = factory.WithSettings(("Groups:MaxMembersPerGroup", "2"));
        var ana = await small.NewPersonAsync("Ana");
        var beto = await small.NewPersonAsync("Beto");
        var carla = await small.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto);

        var join = await carla.JoinRawAsync(group.InviteCode!);
        var profile = await ana.PostAsync(group.Url("/members"), new { displayName = "Vovó Rosa" });

        Assert.Equal(HttpStatusCode.Conflict, join.StatusCode);
        Assert.Equal("group.full", await join.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, profile.StatusCode);
        Assert.Equal("group.full", await profile.ReadCodeAsync());

        // Quem sai libera a vaga.
        await beto.DeleteAsync(group.Url("/members/me"));
        await carla.JoinAsync(group.InviteCode!);
    }

    [Fact]
    public async Task The_person_limit_of_groups_also_applies_to_joining()
    {
        var small = factory.WithSettings(("Groups:MaxGroupsPerUser", "1"));
        var ana = await small.NewPersonAsync("Ana");
        var beto = await small.NewPersonAsync("Beto");
        await beto.CreateGroupAsync("Já tenho um");
        var group = await ana.CreateGroupAsync();

        var response = await beto.JoinRawAsync(group.InviteCode!);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("group.limit_reached", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Guessing_codes_is_rate_limited_per_person_for_join_and_lookup_together()
    {
        var limited = factory.WithSettings(("RateLimiting:Enabled", "true"), ("RateLimiting:GroupJoinPerMinute", "3"));
        var ana = await limited.NewPersonAsync("Ana");
        var beto = await limited.NewPersonAsync("Beto");

        var statuses = new List<HttpStatusCode>
        {
            (await ana.JoinRawAsync("ZZZZZZZZ")).StatusCode,
            (await ana.PostAsync("/api/v1/groups/lookup", new { code = "ZZZZZZZY" })).StatusCode,
            (await ana.JoinRawAsync("ZZZZZZZX")).StatusCode,
            (await ana.JoinRawAsync("ZZZZZZZW")).StatusCode,
        };

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.JoinRawAsync("ZZZZZZZZ")).StatusCode); // outra pessoa tem a própria cota
    }

    [Fact]
    public async Task Two_simultaneous_joins_by_the_same_person_leave_a_single_membership()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var responses = await Task.WhenAll(beto.JoinRawAsync(group.InviteCode!), beto.JoinRawAsync(group.InviteCode!));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("group.concurrent_update", await conflict.ReadCodeAsync());
        }

        var rows = await factory.WithDbAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.UserId == beto.UserId));
        Assert.Equal(1, rows);
    }
}
