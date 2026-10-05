using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Domain.Tests;

public sealed class GroupPermissionsTests
{
    [Theory]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, true)]
    [InlineData(GroupRole.Member, false)]
    public void Admins_and_the_owner_manage_the_group_the_invite_and_the_profiles(GroupRole role, bool allowed)
    {
        Assert.Equal(allowed, GroupPermissions.CanEditGroup(role));
        Assert.Equal(allowed, GroupPermissions.CanManageInvite(role));
        Assert.Equal(allowed, GroupPermissions.CanManageProfiles(role));
    }

    [Theory]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, false)]
    [InlineData(GroupRole.Member, false)]
    public void Only_the_owner_changes_roles_transfers_and_deletes(GroupRole role, bool allowed)
    {
        Assert.Equal(allowed, GroupPermissions.CanChangeRoles(role));
        Assert.Equal(allowed, GroupPermissions.CanTransferOwnership(role));
        Assert.Equal(allowed, GroupPermissions.CanDeleteGroup(role));
    }

    [Theory]
    [InlineData(GroupRole.Owner, GroupRole.Admin, true)]
    [InlineData(GroupRole.Owner, GroupRole.Member, true)]
    [InlineData(GroupRole.Owner, GroupRole.Owner, false)]
    [InlineData(GroupRole.Admin, GroupRole.Member, true)]
    [InlineData(GroupRole.Admin, GroupRole.Admin, false)]
    [InlineData(GroupRole.Admin, GroupRole.Owner, false)]
    [InlineData(GroupRole.Member, GroupRole.Member, false)]
    [InlineData(GroupRole.Member, GroupRole.Admin, false)]
    [InlineData(GroupRole.Member, GroupRole.Owner, false)]
    public void Removal_follows_the_power_order_and_nobody_removes_the_owner(GroupRole actor, GroupRole target, bool allowed)
    {
        Assert.Equal(allowed, GroupPermissions.CanRemove(actor, target));
    }

    [Fact]
    public void Roles_are_ordered_by_power()
    {
        Assert.True(GroupRole.Owner > GroupRole.Admin);
        Assert.True(GroupRole.Admin > GroupRole.Member);
    }
}
