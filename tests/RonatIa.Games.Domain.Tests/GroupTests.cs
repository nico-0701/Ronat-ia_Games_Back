using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Domain.Tests;

public sealed class GroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_makes_an_active_group_with_the_invite_enabled()
    {
        var group = Group.Create("  Família   Silva ", "ABCDEFGH", Now);

        Assert.NotEqual(Guid.Empty, group.Id);
        Assert.Equal("Família Silva", group.Name);
        Assert.Equal("ABCDEFGH", group.InviteCode);
        Assert.True(group.InviteEnabled);
        Assert.False(group.IsDeleted);
        Assert.Equal(Now, group.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    [InlineData("Nome​invisível")]
    [InlineData("Um nome de grupo comprido demais para caber no limite")]
    public void Create_rejects_invalid_names(string name)
    {
        var exception = Assert.Throws<AppException>(() => Group.Create(name, "ABCDEFGH", Now));

        Assert.Equal("group.name_invalid", exception.Code);
        Assert.Equal(ErrorKind.Validation, exception.Kind);
    }

    [Fact]
    public void Replacing_the_invite_code_also_enables_it()
    {
        var group = Group.Create("Amigos", "ABCDEFGH", Now);
        group.SetInviteEnabled(false, Now.AddMinutes(1));

        group.ReplaceInviteCode("JKMNPQRS", Now.AddMinutes(2));

        Assert.Equal("JKMNPQRS", group.InviteCode);
        Assert.True(group.InviteEnabled);
        Assert.Equal(Now.AddMinutes(2), group.UpdatedAt);
    }

    [Fact]
    public void Deleting_is_logical_and_turns_the_invite_off()
    {
        var group = Group.Create("Amigos", "ABCDEFGH", Now);

        group.MarkDeleted(Now.AddDays(1));

        Assert.True(group.IsDeleted);
        Assert.False(group.InviteEnabled);
        Assert.Equal(Now.AddDays(1), group.DeletedAt);
    }

    [Fact]
    public void Rename_normalizes_and_validates()
    {
        var group = Group.Create("Amigos", "ABCDEFGH", Now);

        group.Rename("  Primos  ", Now.AddMinutes(1));
        Assert.Equal("Primos", group.Name);

        Assert.Equal("group.name_invalid", Assert.Throws<AppException>(() => group.Rename("x", Now)).Code);
        Assert.Equal("Primos", group.Name);
    }
}
