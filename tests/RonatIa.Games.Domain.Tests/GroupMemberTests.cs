using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Domain.Tests;

public sealed class GroupMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid GroupId = Guid.CreateVersion7();

    private static GroupMember Account(GroupRole role = GroupRole.Member) => role == GroupRole.Owner
        ? GroupMember.CreateOwner(GroupId, Guid.CreateVersion7(), Now)
        : GroupMember.Join(GroupId, Guid.CreateVersion7(), Now);

    private static GroupMember Profile(string name = "Vovó Rosa", string? preset = null) =>
        GroupMember.CreateProfile(GroupId, name, preset, Now);

    [Fact]
    public void An_account_member_has_no_profile_fields()
    {
        var member = Account();

        Assert.True(member.HasAccount);
        Assert.True(member.IsActive);
        Assert.Null(member.DisplayName);
        Assert.Null(member.AvatarPreset);
        Assert.Null(member.AvatarPhotoId);
        Assert.Equal(GroupRole.Member, member.Role);
        Assert.Null(member.LeftAt);
    }

    [Fact]
    public void The_creator_is_the_owner()
    {
        Assert.Equal(GroupRole.Owner, Account(GroupRole.Owner).Role);
    }

    [Fact]
    public void A_profile_has_a_normalized_name_and_the_default_avatar()
    {
        var profile = Profile("  Vovó    Rosa ");

        Assert.False(profile.HasAccount);
        Assert.Null(profile.UserId);
        Assert.Equal("Vovó Rosa", profile.DisplayName);
        Assert.Equal(AvatarPresets.Default, profile.AvatarPreset);
        Assert.Null(profile.AvatarPhotoId);
        Assert.Equal(GroupRole.Member, profile.Role);
    }

    [Theory]
    [InlineData("x", null, "member.display_name_invalid")]
    [InlineData("Nome Válido", "preset-99", "avatar.unknown_preset")]
    public void A_profile_rejects_invalid_input(string name, string? preset, string code)
    {
        Assert.Equal(code, Assert.Throws<AppException>(() => Profile(name, preset)).Code);
    }

    [Fact]
    public void Leaving_marks_the_member_as_left_and_rejoining_brings_them_back_as_a_plain_member()
    {
        var member = Account();
        member.MakeAdmin(Now);

        member.Leave(Now.AddDays(1));
        Assert.Equal(MemberStatus.Left, member.Status);
        Assert.Equal(Now.AddDays(1), member.LeftAt);
        Assert.False(member.IsActive);

        member.Rejoin(Now.AddDays(2));
        Assert.True(member.IsActive);
        Assert.Null(member.LeftAt);
        Assert.Equal(GroupRole.Member, member.Role); // não volta como administrador
        Assert.Equal(Now, member.JoinedAt);          // a data original é preservada
    }

    [Fact]
    public void The_owner_cannot_leave_or_be_removed()
    {
        var owner = Account(GroupRole.Owner);

        Assert.Equal("group.owner_cannot_leave", Assert.Throws<AppException>(() => owner.Leave(Now)).Code);
        Assert.Equal("group.owner_cannot_leave", Assert.Throws<AppException>(() => owner.Remove(Now)).Code);
        Assert.True(owner.IsActive);
    }

    [Fact]
    public void Removing_a_member_keeps_the_row_with_status_removed()
    {
        var member = Account();

        member.Remove(Now.AddHours(1));

        Assert.Equal(MemberStatus.Removed, member.Status);
        Assert.Equal(Now.AddHours(1), member.LeftAt);
    }

    [Fact]
    public void Removing_a_profile_drops_its_photo_but_keeps_the_name()
    {
        var profile = Profile();
        profile.UseAvatarPhoto(Guid.CreateVersion7(), Now);

        profile.Remove(Now.AddHours(1));

        Assert.Null(profile.AvatarPhotoId);
        Assert.Equal(AvatarPresets.Default, profile.AvatarPreset);
        Assert.Equal("Vovó Rosa", profile.DisplayName);
    }

    [Fact]
    public void Force_leave_ends_an_ownership_too_for_account_deletion()
    {
        var owner = Account(GroupRole.Owner);

        owner.ForceLeave(Now.AddDays(1));

        Assert.Equal(MemberStatus.Left, owner.Status);
        Assert.Equal(GroupRole.Member, owner.Role);
    }

    [Fact]
    public void Claiming_turns_a_profile_into_an_account_member_and_clears_the_profile_fields()
    {
        var profile = Profile();
        var userId = Guid.CreateVersion7();

        profile.Claim(userId, Now.AddHours(1));

        Assert.True(profile.HasAccount);
        Assert.Equal(userId, profile.UserId);
        Assert.Null(profile.DisplayName);
        Assert.Null(profile.AvatarPreset);
        Assert.Null(profile.AvatarPhotoId);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public void A_profile_that_was_claimed_or_removed_cannot_be_claimed_again()
    {
        var claimed = Profile();
        claimed.Claim(Guid.CreateVersion7(), Now);
        var removed = Profile();
        removed.Remove(Now);

        Assert.Equal("group.claim_unavailable", Assert.Throws<AppException>(() => claimed.Claim(Guid.CreateVersion7(), Now)).Code);
        Assert.Equal("group.claim_unavailable", Assert.Throws<AppException>(() => removed.Claim(Guid.CreateVersion7(), Now)).Code);
    }

    [Fact]
    public void Roles_can_be_changed_only_for_members_with_an_account()
    {
        var member = Account();
        member.MakeAdmin(Now);
        Assert.Equal(GroupRole.Admin, member.Role);
        member.MakeMember(Now);
        Assert.Equal(GroupRole.Member, member.Role);

        Assert.Equal("member.no_account", Assert.Throws<AppException>(() => Profile().MakeAdmin(Now)).Code);
    }

    [Fact]
    public void Ownership_moves_by_stepping_down_and_making_the_other_owner()
    {
        var owner = Account(GroupRole.Owner);
        var other = Account();

        owner.StepDownToAdmin(Now);
        other.MakeOwner(Now);

        Assert.Equal(GroupRole.Admin, owner.Role);
        Assert.Equal(GroupRole.Owner, other.Role);
    }

    [Fact]
    public void Profile_edits_work_for_profiles_and_are_refused_for_account_members()
    {
        var profile = Profile();
        profile.RenameProfile("  Tia   Marta ", Now);
        profile.UseAvatarPreset("preset-4", Now);
        Assert.Equal("Tia Marta", profile.DisplayName);
        Assert.Equal("preset-4", profile.AvatarPreset);

        var photo = Guid.CreateVersion7();
        profile.UseAvatarPhoto(photo, Now);
        Assert.Equal(photo, profile.AvatarPhotoId);
        Assert.Null(profile.AvatarPreset);

        var account = Account();
        Assert.Equal("member.has_account", Assert.Throws<AppException>(() => account.RenameProfile("Outro Nome", Now)).Code);
        Assert.Equal("member.has_account", Assert.Throws<AppException>(() => account.UseAvatarPreset("preset-2", Now)).Code);
        Assert.Equal("member.has_account", Assert.Throws<AppException>(() => account.UseAvatarPhoto(photo, Now)).Code);
    }

    [Fact]
    public void Every_state_change_bumps_the_version_for_optimistic_concurrency()
    {
        var member = Account();
        var version = member.Version;

        member.MakeAdmin(Now);
        Assert.Equal(version + 1, member.Version);

        member.Leave(Now);
        Assert.Equal(version + 2, member.Version);

        member.Rejoin(Now);
        Assert.Equal(version + 3, member.Version);
    }
}
