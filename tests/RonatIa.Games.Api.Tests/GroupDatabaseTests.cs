using Microsoft.EntityFrameworkCore;
using Npgsql;
using RonatIa.Games.Api.Tests.Infrastructure;
using RonatIa.Games.Infrastructure.Persistence;

namespace RonatIa.Games.Api.Tests;

/// <summary>As regras que o banco garante sozinho, mesmo que o código da aplicação erre.</summary>
public sealed class GroupDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string?> FailedConstraintAsync(Func<AppDbContext, Task> action)
    {
        try
        {
            await factory.WithDbAsync(action);
        }
        catch (Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is PostgresException postgres)
                {
                    return postgres.ConstraintName;
                }
            }

            throw;
        }

        return null;
    }

    private static Task InsertMemberAsync(AppDbContext db, Guid groupId, Guid? userId, string role = "member", string status = "active", string? name = null, string? preset = null, DateTimeOffset? leftAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app.group_members (id, group_id, user_id, display_name, avatar_preset, avatar_photo_id, role, status, joined_at, left_at, updated_at, version)
            VALUES ({Guid.CreateVersion7()}, {groupId}, {userId}, {name}, {preset}, NULL, {role}, {status}, {now}, {leftAt}, {now}, 0)
            """);
    }

    private static Task InsertGroupAsync(AppDbContext db, string code)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO app.groups (id, name, invite_code, invite_enabled, created_at, updated_at) VALUES ({Guid.CreateVersion7()}, 'Grupo', {code}, true, {now}, {now})");
    }

    [Fact]
    public async Task There_can_be_only_one_active_owner_per_group()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var constraint = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, role: "owner"));

        Assert.Equal("ux_group_members_one_active_owner", constraint);
    }

    [Fact]
    public async Task A_former_owner_row_does_not_count_as_an_active_owner()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var constraint = await FailedConstraintAsync(db =>
            InsertMemberAsync(db, group.Id, beto.UserId, role: "owner", status: "left", leftAt: DateTimeOffset.UtcNow));

        Assert.Null(constraint);
    }

    [Fact]
    public async Task A_person_has_at_most_one_row_per_group()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var constraint = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, ana.UserId));

        Assert.Equal("ux_group_members_group_user", constraint);
    }

    [Fact]
    public async Task Profiles_without_an_account_can_repeat_freely()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var first = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, name: "Vovó", preset: "preset-1"));
        var second = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, name: "Vovó", preset: "preset-1"));

        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task An_owner_must_have_an_account_and_a_profile_must_be_a_plain_member()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.group_members SET status = 'left', left_at = now() WHERE group_id = {group.Id}"));

        var owner = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, role: "owner", name: "Dono sem conta", preset: "preset-1"));
        var admin = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, role: "admin", name: "Admin sem conta", preset: "preset-1"));

        Assert.Contains(owner, new[] { "ck_group_members_owner_has_account", "ck_group_members_profile_is_member" });
        Assert.Equal("ck_group_members_profile_is_member", admin);
    }

    [Fact]
    public async Task Profile_fields_must_be_consistent_with_having_an_account()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var accountWithName = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, name: "Nome duplicado"));
        var profileWithoutName = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, preset: "preset-1"));
        var profileWithoutAvatar = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, null, name: "Sem avatar"));

        Assert.Equal("ck_group_members_profile_fields", accountWithName);
        Assert.Equal("ck_group_members_profile_fields", profileWithoutName);
        Assert.Equal("ck_group_members_profile_fields", profileWithoutAvatar);
    }

    [Fact]
    public async Task Status_role_and_left_at_must_agree()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var beto = await factory.NewPersonAsync("Beto");
        var group = await ana.CreateGroupAsync();

        var activeWithLeftAt = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, leftAt: DateTimeOffset.UtcNow));
        var leftWithoutDate = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, status: "left"));
        var badRole = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, role: "deus"));
        var badStatus = await FailedConstraintAsync(db => InsertMemberAsync(db, group.Id, beto.UserId, status: "banido", leftAt: DateTimeOffset.UtcNow));

        Assert.Equal("ck_group_members_left_at", activeWithLeftAt);
        Assert.Equal("ck_group_members_left_at", leftWithoutDate);
        Assert.Equal("ck_group_members_role", badRole);
        Assert.Equal("ck_group_members_status", badStatus);
    }

    [Theory]
    [InlineData("abcdefgh")]   // minúsculas
    [InlineData("ABCDEFGU")]   // U fora do alfabeto
    [InlineData("ABCDEFG1")]   // 1 fora do alfabeto
    [InlineData("ABCDEFGI")]   // I fora do alfabeto
    [InlineData("ABCDEFG")]    // curto
    public async Task The_invite_code_must_use_the_agreed_alphabet(string code)
    {
        var constraint = await FailedConstraintAsync(db => InsertGroupAsync(db, code));

        // Um código curto ("ABCDEFG") vira "ABCDEFG " em char(8) e também é recusado pelo padrão.
        Assert.Equal("ck_groups_invite_code", constraint);
    }

    [Fact]
    public async Task The_invite_code_is_unique_across_all_groups()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();

        var constraint = await FailedConstraintAsync(db => InsertGroupAsync(db, group.InviteCode!));

        Assert.Equal("ux_groups_invite_code", constraint);
    }

    [Fact]
    public async Task Deleting_a_group_row_cascades_to_its_members()
    {
        var ana = await factory.NewPersonAsync("Ana");
        var group = await ana.CreateGroupAsync();
        await ana.AddProfileAsync(group.Id, "Vovó Rosa");

        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.groups WHERE id = {group.Id}"));

        var left = await factory.WithDbAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == group.Id));
        Assert.Equal(0, left);
    }

    [Fact]
    public async Task An_account_with_group_memberships_cannot_be_hard_deleted()
    {
        var ana = await factory.NewPersonAsync("Ana");
        await ana.CreateGroupAsync();

        var constraint = await FailedConstraintAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.users WHERE id = {ana.UserId}"));

        Assert.Equal("fk_group_members_users_user_id", constraint);
    }
}
