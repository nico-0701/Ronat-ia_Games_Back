using System.Security.Cryptography;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Domain.Tests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static User NewUser(string name = "Nicole", string? preset = null) =>
        User.Register(RandomNumberGenerator.GetBytes(32), "4321", name, preset, "2026-10", Now);

    [Fact]
    public void Register_creates_an_active_user_with_the_default_avatar()
    {
        var user = NewUser();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(AvatarPresets.Default, user.AvatarPreset);
        Assert.Null(user.AvatarPhotoId);
        Assert.Equal("4321", user.PhoneLast4);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal("2026-10", user.TermsVersion);
        Assert.False(user.IsAdmin);
    }

    [Fact]
    public void Register_normalizes_the_display_name()
    {
        Assert.Equal("Zé Milton", NewUser("  Zé    Milton  ").DisplayName);
    }

    [Fact]
    public void Register_rejects_an_invalid_name()
    {
        var exception = Assert.Throws<AppException>(() => NewUser("x"));

        Assert.Equal("user.display_name_invalid", exception.Code);
    }

    [Fact]
    public void Register_rejects_an_unknown_avatar_preset()
    {
        var exception = Assert.Throws<AppException>(() => NewUser(preset: "preset-99"));

        Assert.Equal("avatar.unknown_preset", exception.Code);
    }

    [Fact]
    public void Using_a_photo_clears_the_preset_and_using_a_preset_clears_the_photo()
    {
        var user = NewUser(preset: "preset-2");
        var photoId = Guid.CreateVersion7();

        user.UseAvatarPhoto(photoId, Now.AddMinutes(1));
        Assert.Equal(photoId, user.AvatarPhotoId);
        Assert.Null(user.AvatarPreset);

        user.UseAvatarPreset("preset-4", Now.AddMinutes(2));
        Assert.Equal("preset-4", user.AvatarPreset);
        Assert.Null(user.AvatarPhotoId);
        Assert.Equal(Now.AddMinutes(2), user.UpdatedAt);
    }

    [Fact]
    public void Rename_validates_and_updates_the_timestamp()
    {
        var user = NewUser();

        user.Rename(" Nova  Pessoa ", Now.AddHours(1));

        Assert.Equal("Nova Pessoa", user.DisplayName);
        Assert.Equal(Now.AddHours(1), user.UpdatedAt);
        Assert.Throws<AppException>(() => user.Rename("a", Now));
    }

    [Fact]
    public void MarkDeleted_anonymizes_the_account_and_frees_the_phone()
    {
        var user = NewUser();
        var originalHash = user.PhoneHash.ToArray();

        user.MarkDeleted(Now.AddDays(1));

        Assert.Equal(UserStatus.Deleted, user.Status);
        Assert.Equal(User.DeletedDisplayName, user.DisplayName);
        Assert.Equal("0000", user.PhoneLast4);
        Assert.Equal(32, user.PhoneHash.Length);
        Assert.NotEqual(originalHash, user.PhoneHash);
        Assert.Equal(AvatarPresets.Default, user.AvatarPreset);
        Assert.Equal(Now.AddDays(1), user.DeletedAt);
    }

    [Fact]
    public void A_deleted_user_cannot_be_reactivated()
    {
        var user = NewUser();
        user.MarkDeleted(Now);

        var exception = Assert.Throws<AppException>(() => user.Reactivate(Now));

        Assert.Equal(ErrorKind.Conflict, exception.Kind);
    }

    [Fact]
    public void Suspend_and_reactivate_toggle_the_status()
    {
        var user = NewUser();

        user.Suspend(Now);
        Assert.Equal(UserStatus.Suspended, user.Status);

        user.Reactivate(Now);
        Assert.Equal(UserStatus.Active, user.Status);
    }
}
