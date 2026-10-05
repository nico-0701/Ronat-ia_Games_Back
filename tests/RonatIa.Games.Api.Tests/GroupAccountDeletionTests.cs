using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Api.Tests;

/// <summary>O que acontece com os grupos quando a conta é excluída (LGPD).</summary>
public sealed class GroupAccountDeletionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> DeleteAccountAsync(Person person) =>
        person.DeleteAsync("/api/v1/users/me", new { confirmation = "EXCLUIR" });

    [Fact]
    public async Task An_owner_of_a_group_with_other_people_must_transfer_first()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync("Família Silva");
        await beto.JoinAsync(group.InviteCode!);

        var blocked = await DeleteAccountAsync(ana);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.ReadProblemAsync();
        Assert.Equal("user.owns_groups", problem.GetProperty("code").GetString());
        Assert.Equal(["Família Silva"], problem.GetProperty("errors").GetProperty("groups").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/v1/users/me")).StatusCode); // a conta continua de pé
        Assert.Equal(GroupRole.Owner, (await ana.GetGroupAsync(group.Id)).MyRole);
    }

    [Fact]
    public async Task After_transferring_the_owner_can_delete_the_account_and_the_group_lives_on()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.PostAsync(group.Url("/transfer-ownership"), new { memberId = group.MemberOf(beto).Id });

        var response = await DeleteAccountAsync(ana);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var remaining = await beto.GetGroupAsync(group.Id);
        Assert.Equal(["Beto"], remaining.Members.Select(m => m.DisplayName));
        Assert.Equal(GroupRole.Owner, remaining.MyRole);
        Assert.Equal(group.InviteCode, remaining.InviteCode);
    }

    [Fact]
    public async Task An_owner_who_is_the_only_person_takes_the_group_with_them()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync("Só eu");
        await ana.AddProfileAsync(group.Id, "Vovó Rosa"); // perfis sem conta não contam como pessoas

        var response = await DeleteAccountAsync(ana);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("group.invalid_code", await (await beto.JoinRawAsync(group.InviteCode!)).ReadCodeAsync());
        var stored = await factory.WithDbAsync(db => db.Groups.AsNoTracking().SingleAsync(g => g.Id == group.Id));
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task A_plain_member_leaves_every_group_but_the_history_row_stays()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var carla = await factory.NewPersonAsync("Carla");
        var beto = await factory.NewPersonAsync("Beto");
        var first = await ana.GroupWithAsync(beto);
        var second = await carla.CreateGroupAsync("Outro");
        await beto.JoinAsync(second.InviteCode!);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccountAsync(beto)).StatusCode);

        Assert.Equal(["Ana"], (await ana.GetGroupAsync(first.Id)).Members.Select(m => m.DisplayName));
        Assert.Equal(["Carla"], (await carla.GetGroupAsync(second.Id)).Members.Select(m => m.DisplayName));
        var rows = await factory.WithDbAsync(db => db.GroupMembers.AsNoTracking().Where(m => m.UserId == beto.UserId).ToListAsync());
        Assert.Equal(2, rows.Count); // o histórico continua apontando para o membro
        Assert.All(rows, row => Assert.Equal(MemberStatus.Left, row.Status));
    }

    [Fact]
    public async Task An_admin_deleting_the_account_just_leaves()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.GroupWithAsync(beto);
        await ana.PatchAsync(group.Url($"/members/{group.MemberOf(beto).Id}"), new { role = "admin" });

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccountAsync(beto)).StatusCode);

        Assert.Single((await ana.GetGroupAsync(group.Id)).Members);
    }

    [Fact]
    public async Task The_same_phone_can_start_over_without_the_old_groups()
    {
        var phone = TestPhones.Next();
        var auth = await factory.CreateClient().RegisterAsync(phone, "Ana Antiga");
        var ana = new Person { Auth = auth, Client = factory.ClientFor(auth.AccessToken) };
        await ana.CreateGroupAsync("Antigo");
        await DeleteAccountAsync(ana);

        var again = await factory.CreateClient().RegisterAsync(phone, "Ana Nova");
        var anaAgain = new Person { Auth = again, Client = factory.ClientFor(again.AccessToken) };

        Assert.Empty(await anaAgain.ListGroupsAsync());
        Assert.NotEqual(ana.UserId, anaAgain.UserId);
    }

    [Fact]
    public async Task A_refused_deletion_changes_nothing_at_all()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var mine = await ana.CreateGroupAsync("Com gente");
        await beto.JoinAsync(mine.InviteCode!);
        var other = await beto.CreateGroupAsync("Do Beto");
        await ana.JoinAsync(other.InviteCode!);

        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAccountAsync(ana)).StatusCode);

        // Nem os vínculos das outras associações foram encerrados pela tentativa que falhou.
        Assert.Equal(2, (await ana.ListGroupsAsync()).Count);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/v1/users/me")).StatusCode);
    }
}
