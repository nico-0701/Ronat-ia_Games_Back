using System.Net;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Groups;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

/// <summary>Papéis, remoção de membros e transferência de propriedade.</summary>
public sealed class GroupRolesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> SetRoleAsync(Person actor, GroupDetailDto group, Guid memberId, string role) =>
        actor.PatchAsync(group.Url($"/members/{memberId}"), new { role });

    [Fact]
    public async Task The_owner_promotes_a_member_to_admin_and_back()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var betoId = group.MemberOf(beto).Id;

        var promoted = await SetRoleAsync(ana, group, betoId, "admin");
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(GroupRole.Admin, (await promoted.ReadAsAsync<MemberDto>()).Role);
        Assert.Equal(GroupRole.Admin, (await beto.GetGroupAsync(group.Id)).MyRole);

        var demoted = await SetRoleAsync(ana, group, betoId, "member");
        Assert.Equal(GroupRole.Member, (await demoted.ReadAsAsync<MemberDto>()).Role);
        Assert.Equal(GroupRole.Member, (await beto.GetGroupAsync(group.Id)).MyRole);
    }

    [Fact]
    public async Task Only_the_owner_changes_roles()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "admin");

        var byAdmin = await SetRoleAsync(beto, group, group.MemberOf(carla).Id, "admin");
        var byMember = await SetRoleAsync(carla, group, group.MemberOf(beto).Id, "member");

        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);
        Assert.Equal("group.forbidden", await byAdmin.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        var current = await ana.GetGroupAsync(group.Id);
        Assert.Equal(GroupRole.Admin, current.MemberOf(beto).Role);
        Assert.Equal(GroupRole.Member, current.MemberOf(carla).Role);
    }

    [Fact]
    public async Task Role_changes_follow_the_rules()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var profile = await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        var toOwner = await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "owner");
        var ownerDemotion = await SetRoleAsync(ana, group, group.MemberOf(ana).Id, "admin");
        var profileAdmin = await SetRoleAsync(ana, group, profile.Id, "admin");
        var unknownRole = await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "deus");

        Assert.Equal("group.invalid_role", await toOwner.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, toOwner.StatusCode);
        Assert.Equal("group.owner_role_fixed", await ownerDemotion.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, ownerDemotion.StatusCode);
        Assert.Equal("member.no_account", await profileAdmin.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, profileAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Equal("validation.failed", await unknownRole.ReadCodeAsync());

        var current = await ana.GetGroupAsync(group.Id);
        Assert.Equal(GroupRole.Owner, current.MyRole);
        Assert.Equal(GroupRole.Member, current.MemberOf(beto).Role);
    }

    [Fact]
    public async Task Admins_remove_plain_members_and_profiles_but_not_other_admins_or_the_owner()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var davi = await factory.NewPersonAsync("Davi");
        var group = await ana.GroupWithAsync(beto, carla, davi);
        var profile = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "admin");
        await SetRoleAsync(ana, group, group.MemberOf(carla).Id, "admin");

        var removeAdmin = await beto.DeleteAsync(group.Url($"/members/{group.MemberOf(carla).Id}"));
        var removeOwner = await beto.DeleteAsync(group.Url($"/members/{group.MemberOf(ana).Id}"));
        var removeMember = await beto.DeleteAsync(group.Url($"/members/{group.MemberOf(davi).Id}"));
        var removeProfile = await beto.DeleteAsync(group.Url($"/members/{profile.Id}"));

        Assert.Equal(HttpStatusCode.Forbidden, removeAdmin.StatusCode);
        Assert.Equal("group.forbidden", await removeAdmin.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, removeOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removeMember.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removeProfile.StatusCode);
        Assert.Equal(["Ana", "Beto", "Carla"], (await ana.GetGroupAsync(group.Id)).Members.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task The_owner_removes_admins_and_members_but_never_themselves_that_way()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "admin");

        var removeAdmin = await ana.DeleteAsync(group.Url($"/members/{group.MemberOf(beto).Id}"));
        var removeMember = await ana.DeleteAsync(group.Url($"/members/{group.MemberOf(carla).Id}"));
        var removeSelf = await ana.DeleteAsync(group.Url($"/members/{group.MemberOf(ana).Id}"));

        Assert.Equal(HttpStatusCode.NoContent, removeAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removeMember.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeSelf.StatusCode);
        Assert.Equal("group.owner_cannot_leave", await removeSelf.ReadCodeAsync());
        Assert.Single((await ana.GetGroupAsync(group.Id)).Members);
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once_and_a_plain_member_removes_nobody()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);

        var byMember = await beto.DeleteAsync(group.Url($"/members/{group.MemberOf(carla).Id}"));
        await ana.DeleteAsync(group.Url($"/members/{group.MemberOf(beto).Id}"));

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync(group.Url())).StatusCode);
        Assert.Empty(await beto.ListGroupsAsync());
        Assert.Equal(HttpStatusCode.OK, (await carla.GetAsync(group.Url())).StatusCode);
    }

    [Fact]
    public async Task Removing_oneself_by_id_is_the_same_as_leaving()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);

        var response = await beto.DeleteAsync(group.Url($"/members/{group.MemberOf(beto).Id}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var status = await factory.WithDbAsync(db => db.GroupMembers.Where(m => m.UserId == beto.UserId).Select(m => m.Status).SingleAsync());
        Assert.Equal(MemberStatus.Left, status);
    }

    [Fact]
    public async Task Unknown_members_and_members_of_other_groups_are_not_found()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync("Um");
        var other = await beto.CreateGroupAsync("Outro");

        var unknown = await ana.DeleteAsync(group.Url($"/members/{Guid.NewGuid()}"));
        var foreign = await ana.DeleteAsync(group.Url($"/members/{other.MyMemberId}"));
        var foreignRole = await SetRoleAsync(ana, group, other.MyMemberId, "admin");

        Assert.All(new[] { unknown, foreign, foreignRole }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("member.not_found", await foreign.ReadCodeAsync());
        Assert.Equal(GroupRole.Owner, (await beto.GetGroupAsync(other.Id)).MyRole); // o outro grupo não foi tocado
    }

    // ---- transferência de propriedade ----

    [Fact]
    public async Task The_owner_transfers_ownership_and_becomes_an_admin()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);

        var response = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id });
        var asSeenByAna = await response.ReadAsAsync<GroupDetailDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GroupRole.Admin, asSeenByAna.MyRole);
        Assert.Equal(GroupRole.Owner, asSeenByAna.MemberOf(beto).Role);
        Assert.Equal(GroupRole.Owner, (await beto.GetGroupAsync(group.Id)).MyRole);

        var owners = await factory.WithDbAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.Role == GroupRole.Owner && m.Status == MemberStatus.Active));
        Assert.Equal(1, owners);
    }

    [Fact]
    public async Task After_a_transfer_the_new_owner_holds_the_power_and_the_old_owner_can_leave()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id });

        var oldOwnerDeletes = await ana.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" });
        var oldOwnerTransfers = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id });
        var oldOwnerRenames = await ana.PatchAsync(group.Url(), new { name = "Ainda Posso" }); // administrador ainda renomeia
        var oldOwnerLeaves = await ana.DeleteAsync(group.Url("/members/me"));
        var newOwnerDeletes = await beto.DeleteAsync(group.Url(), new { confirmation = "EXCLUIR" });

        Assert.Equal(HttpStatusCode.Forbidden, oldOwnerDeletes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, oldOwnerTransfers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldOwnerRenames.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, oldOwnerLeaves.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, newOwnerDeletes.StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_transfer()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);
        await SetRoleAsync(ana, group, group.MemberOf(beto).Id, "admin");

        var byAdmin = await beto.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id });
        var byMember = await carla.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(carla).Id });

        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(GroupRole.Owner, (await ana.GetGroupAsync(group.Id)).MyRole);
    }

    [Fact]
    public async Task Ownership_only_goes_to_another_active_member_with_an_account()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        var profile = await ana.AddProfileAsync(group.Id, "Vovó Rosa");
        var left = await factory.NewPersonAsync("Saiu");
        await left.JoinAsync(group.InviteCode!);
        var leftId = (await ana.GetGroupAsync(group.Id)).MemberOf(left).Id;
        await left.DeleteAsync(group.Url("/members/me"));

        var toSelf = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(ana).Id });
        var toProfile = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = profile.Id });
        var toUnknown = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = Guid.NewGuid() });
        var toLeft = await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = leftId });
        var toNobody = await ana.PostAsync(group.Url("/transfer-ownership"), new { });

        Assert.Equal("group.transfer_invalid", await toSelf.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, toSelf.StatusCode);
        Assert.Equal("group.transfer_invalid", await toProfile.ReadCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, toUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, toLeft.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toNobody.StatusCode);
        Assert.Equal(GroupRole.Owner, (await ana.GetGroupAsync(group.Id)).MyRole);
    }

    [Fact]
    public async Task Two_simultaneous_transfers_leave_exactly_one_owner()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var carla = await factory.NewPersonAsync("Carla");
        var group = await ana.GroupWithAsync(beto, carla);

        var responses = await Task.WhenAll(
            ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id }),
            ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(carla).Id }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
        var owners = await factory.WithDbAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.Role == GroupRole.Owner && m.Status == MemberStatus.Active));
        Assert.Equal(1, owners);
    }
}
