using System.Net;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

/// <summary>Membros sem conta (perfis com nome e foto, geridos por administradores) e a reivindicação deles ao entrar.</summary>
public sealed class GroupProfilesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Guid PhotoId(AvatarDto avatar) => Guid.Parse(avatar.Url!.Split('/')[^1]);

    private Task<bool> PhotoExistsAsync(Guid id) => factory.WithDbAsync(db => db.Avatars.AnyAsync(a => a.Id == id));

    [Fact]
    public async Task An_admin_creates_a_profile_without_an_account()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(beto).Id}"), new { role = "admin" });

        var profile = await beto.AddProfileAsync(group.Id, "  Vovó   Rosa ", "preset-3");

        Assert.Equal("Vovó Rosa", profile.DisplayName);
        Assert.False(profile.HasAccount);
        Assert.False(profile.IsMe);
        Assert.Equal(GroupRole.Member, profile.Role);
        Assert.Equal("preset", profile.Avatar.Kind);
        Assert.Equal("preset-3", profile.Avatar.Preset);

        var detail = await ana.GetGroupAsync(group.Id);
        Assert.Equal(3, detail.Members.Count);
        Assert.Contains(detail.Members, m => m.Id == profile.Id && !m.HasAccount);
        Assert.Equal(3, (await ana.ListGroupsAsync()).Single().MemberCount);
    }

    [Fact]
    public async Task A_profile_gets_the_default_avatar_when_none_is_chosen()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var profile = await ana.AddProfileAsync(group.Id, "Tio Zé");

        Assert.Equal("preset-1", profile.Avatar.Preset);
    }

    [Theory]
    [InlineData("""{"displayName":"x"}""", "member.display_name_invalid")]
    [InlineData("""{"displayName":"Nome​invisível"}""", "member.display_name_invalid")]
    [InlineData("""{"displayName":"Nome Válido","avatarPreset":"preset-99"}""", "avatar.unknown_preset")]
    [InlineData("""{}""", "validation.failed")]
    public async Task Creating_a_profile_validates_the_input(string json, string code)
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var response = await ana.Client.PostAsync(group.Url("/members"), new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ReadCodeAsync());
        Assert.Single((await ana.GetGroupAsync(group.Id)).Members);
    }

    [Fact]
    public async Task A_plain_member_cannot_manage_profiles()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var create = await beto.PostAsync(group.Url("/members"), new { displayName = "Intruso" });
        var rename = await beto.PatchAsync(group.Url($"/members/{vovo.Id}"), new { displayName = "Outro Nome" });
        var photo = await beto.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg());
        var clear = await beto.DeleteAsync(group.Url($"/members/{vovo.Id}/avatar"));
        var remove = await beto.DeleteAsync(group.Url($"/members/{vovo.Id}"));

        Assert.All(new[] { create, rename, photo, clear, remove }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal("Vovó Rosa", (await ana.GetGroupAsync(group.Id)).Members.Single(m => !m.HasAccount).DisplayName);
    }

    [Fact]
    public async Task An_admin_renames_a_profile_and_picks_another_avatar()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var response = await ana.PatchAsync(group.Url($"/members/{vovo.Id}"), new { displayName = "  Vovó   Rosinha ", avatarPreset = "preset-5" });
        var updated = await response.ReadAsAsync<MemberDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(vovo.Id, updated.Id);
        Assert.Equal("Vovó Rosinha", updated.DisplayName);
        Assert.Equal("preset-5", updated.Avatar.Preset);
    }

    [Fact]
    public async Task Invalid_profile_edits_are_refused_and_change_nothing()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa", "preset-2");

        var badName = await ana.PatchAsync(group.Url($"/members/{vovo.Id}"), new { displayName = "x" });
        var badPreset = await ana.PatchAsync(group.Url($"/members/{vovo.Id}"), new { avatarPreset = "preset-99" });
        var unknown = await ana.PatchAsync(group.Url($"/members/{Guid.NewGuid()}"), new { displayName = "Quem?" });

        Assert.Equal("member.display_name_invalid", await badName.ReadCodeAsync());
        Assert.Equal("avatar.unknown_preset", await badPreset.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("member.not_found", await unknown.ReadCodeAsync());
        var current = (await ana.GetGroupAsync(group.Id)).Members.Single(m => !m.HasAccount);
        Assert.Equal("Vovó Rosa", current.DisplayName);
        Assert.Equal("preset-2", current.Avatar.Preset);
    }

    [Fact]
    public async Task People_with_an_account_change_their_own_name_in_their_profile_not_through_the_group()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var betoMember = group.MemberOf(beto).Id;

        var rename = await ana.PatchAsync(group.Url($"/members/{betoMember}"), new { displayName = "Beto Renomeado" });
        var preset = await ana.PatchAsync(group.Url($"/members/{betoMember}"), new { avatarPreset = "preset-4" });
        var photo = await ana.PutFileAsync(group.Url($"/members/{betoMember}/avatar"), TestImages.Jpeg());
        var clear = await ana.DeleteAsync(group.Url($"/members/{betoMember}/avatar"));

        Assert.All(new[] { rename, preset, photo, clear }, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal("member.has_account", await rename.ReadCodeAsync());
        Assert.Equal("Beto", (await ana.GetGroupAsync(group.Id)).MemberOf(beto).DisplayName);
    }

    [Fact]
    public async Task A_profile_photo_is_processed_like_an_account_photo_and_replaced_cleanly()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var first = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg(300, 200))).ReadAsAsync<MemberDto>();
        Assert.Equal("photo", first.Avatar.Kind);
        Assert.Null(first.Avatar.Preset);

        var image = await factory.CreateClient().GetAsync(first.Avatar.Url!);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
        using var decoded = TestImages.Decode(await image.Content.ReadAsByteArrayAsync());
        Assert.Equal((256, 256), (decoded.Width, decoded.Height));

        var second = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Png(), "outra.png", "image/png")).ReadAsAsync<MemberDto>();
        Assert.NotEqual(first.Avatar.Url, second.Avatar.Url);
        Assert.False(await PhotoExistsAsync(PhotoId(first.Avatar)));
        Assert.True(await PhotoExistsAsync(PhotoId(second.Avatar)));

        var cleared = await (await ana.DeleteAsync(group.Url($"/members/{vovo.Id}/avatar"))).ReadAsAsync<MemberDto>();
        Assert.Equal("preset", cleared.Avatar.Kind);
        Assert.Equal("preset-1", cleared.Avatar.Preset);
        Assert.False(await PhotoExistsAsync(PhotoId(second.Avatar)));
    }

    [Fact]
    public async Task A_profile_photo_must_be_a_real_image()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var response = await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), System.Text.Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("avatar.invalid_image", await response.ReadCodeAsync());
        Assert.Equal("preset", (await ana.GetGroupAsync(group.Id)).Members.Single(m => !m.HasAccount).Avatar.Kind);
    }

    [Fact]
    public async Task Choosing_a_preset_for_a_profile_discards_its_photo()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        var photo = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg())).ReadAsAsync<MemberDto>();

        var updated = await (await ana.PatchAsync(group.Url($"/members/{vovo.Id}"), new { avatarPreset = "preset-6" })).ReadAsAsync<MemberDto>();

        Assert.Equal("preset-6", updated.Avatar.Preset);
        Assert.False(await PhotoExistsAsync(PhotoId(photo.Avatar)));
    }

    [Fact]
    public async Task Changing_my_own_photo_does_not_delete_a_photo_a_profile_still_uses()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        var vovoPhoto = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg())).ReadAsAsync<MemberDto>();

        // As fotos da conta e do perfil vivem na mesma tabela; trocar uma não pode apagar a outra.
        await ana.PutFileAsync("/api/v1/users/me/avatar", TestImages.Jpeg());
        await ana.PutFileAsync("/api/v1/users/me/avatar", TestImages.Png(), "nova.png", "image/png");
        await ana.DeleteAsync("/api/v1/users/me/avatar");

        Assert.True(await PhotoExistsAsync(PhotoId(vovoPhoto.Avatar)));
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync(vovoPhoto.Avatar.Url!)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_profile_deletes_its_photo_and_hides_it_from_the_group()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        var photo = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg())).ReadAsAsync<MemberDto>();

        var response = await ana.DeleteAsync(group.Url($"/members/{vovo.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single((await ana.GetGroupAsync(group.Id)).Members);
        Assert.False(await PhotoExistsAsync(PhotoId(photo.Avatar)));
    }

    // ---- reivindicar um perfil ao entrar ----

    [Fact]
    public async Task Joining_with_a_claim_turns_the_profile_into_the_persons_own_membership()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto Real");
        var group = await ana.CreateGroupAsync();
        var profile = await ana.AddProfileAsync(group.Id, "Beto (perfil)", "preset-4");

        var joined = await beto.JoinAsync(group.InviteCode!, claimMemberId: profile.Id);

        Assert.Equal(profile.Id, joined.MyMemberId); // o mesmo membro: o histórico vem junto
        Assert.Equal(GroupRole.Member, joined.MyRole);
        Assert.Equal(2, joined.Members.Count);         // o perfil virou a pessoa, não um terceiro membro
        var me = joined.Members.Single(m => m.IsMe);
        Assert.Equal("Beto Real", me.DisplayName);     // passa a valer o nome e o avatar da conta
        Assert.True(me.HasAccount);
        Assert.DoesNotContain(joined.Members, m => !m.HasAccount);
        Assert.Equal(2, (await ana.ListGroupsAsync()).Single().MemberCount);
    }

    [Fact]
    public async Task A_claimed_profile_is_no_longer_offered_and_cannot_be_claimed_again()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.CreateGroupAsync();
        var profile = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        await beto.JoinAsync(group.InviteCode!, profile.Id);

        var preview = await (await carla.PostAsync("/api/v1/groups/lookup", new { code = group.InviteCode })).ReadAsAsync<GroupPreviewDto>();
        var again = await carla.JoinRawAsync(group.InviteCode!, profile.Id);

        Assert.Empty(preview.ClaimableMembers);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("group.claim_unavailable", await again.ReadCodeAsync());
        Assert.Empty(await carla.ListGroupsAsync()); // a tentativa falha por inteiro: carla não entrou
    }

    [Fact]
    public async Task Only_active_profiles_of_that_same_group_can_be_claimed()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(carla);
        var other = await ana.CreateGroupAsync("Outro grupo");
        var otherProfile = await ana.AddProfileAsync(other.Id, "Perfil de outro grupo");
        var removed = await ana.AddProfileAsync(group.Id, "Perfil removido");
        await ana.DeleteAsync(group.Url($"/members/{removed.Id}"));

        var attempts = new[]
        {
            Guid.NewGuid(),                       // não existe
            group.MemberOf(carla).Id,             // é uma pessoa com conta, não um perfil
            group.MemberOf(ana).Id,               // é o dono
            otherProfile.Id,                      // pertence a outro grupo
            removed.Id,                           // foi removido
        };

        foreach (var id in attempts)
        {
            var response = await beto.JoinRawAsync(group.InviteCode!, id);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("group.claim_unavailable", await response.ReadCodeAsync());
        }

        Assert.Empty(await beto.ListGroupsAsync());
        Assert.Equal(2, (await ana.GetGroupAsync(group.Id)).Members.Count); // nada mudou no grupo
    }

    [Fact]
    public async Task Claiming_when_already_a_member_is_a_conflict_and_changes_nothing()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var response = await beto.JoinRawAsync(group.InviteCode!, vovo.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("group.already_member", await response.ReadCodeAsync());
        Assert.Contains((await ana.GetGroupAsync(group.Id)).Members, m => m.Id == vovo.Id && !m.HasAccount);
    }

    [Fact]
    public async Task Someone_who_left_before_cannot_claim_a_profile_on_the_way_back()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        await beto.DeleteAsync(group.Url("/members/me"));

        var response = await beto.JoinRawAsync(group.InviteCode!, vovo.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("group.claim_unavailable", await response.ReadCodeAsync());
        Assert.Empty(await beto.ListGroupsAsync());
    }

    [Fact]
    public async Task Claiming_discards_the_profile_photo()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        var photo = await (await ana.PutFileAsync(group.Url($"/members/{vovo.Id}/avatar"), TestImages.Jpeg())).ReadAsAsync<MemberDto>();

        await beto.JoinAsync(group.InviteCode!, vovo.Id);

        Assert.False(await PhotoExistsAsync(PhotoId(photo.Avatar)));
    }

    [Fact]
    public async Task A_full_group_still_accepts_a_claim_because_the_size_does_not_change()
    {
        var small = factory.WithSettings(("Groups:MaxMembersPerGroup", "2"));
        var ana = await small.NewPersonAsync("Ana");
        var beto = await small.NewPersonAsync("Beto");
        var carla = await small.NewPersonAsync("Carla");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa"); // grupo cheio: 2 de 2

        var plain = await carla.JoinRawAsync(group.InviteCode!);
        var claim = await beto.JoinRawAsync(group.InviteCode!, vovo.Id);

        Assert.Equal("group.full", await plain.ReadCodeAsync()); // entrar como novo membro não cabe
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);       // assumir o perfil cabe
        Assert.Equal(2, (await ana.GetGroupAsync(group.Id)).Members.Count);
    }

    [Fact]
    public async Task A_claim_still_respects_the_persons_group_limit()
    {
        var small = factory.WithSettings(("Groups:MaxGroupsPerUser", "1"));
        var ana = await small.NewPersonAsync("Ana");
        var beto = await small.NewPersonAsync("Beto");
        await beto.CreateGroupAsync("Já tenho um");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var response = await beto.JoinRawAsync(group.InviteCode!, vovo.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("group.limit_reached", await response.ReadCodeAsync());
    }

    [Fact]
    public async Task Two_people_claiming_the_same_profile_at_once_only_one_wins()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.CreateGroupAsync();
        var vovo = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var responses = await Task.WhenAll(beto.JoinRawAsync(group.InviteCode!, vovo.Id), carla.JoinRawAsync(group.InviteCode!, vovo.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("group.claim_unavailable", await loser.ReadCodeAsync());
        var rows = await factory.WithDbAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.Status == MemberStatus.Active));
        Assert.Equal(2, rows); // Ana e quem venceu
    }
}
