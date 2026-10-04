using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Users;

public static class AvatarUrls
{
    /// <summary>Caminho relativo da foto de avatar (não adivinhável: o id é aleatório).</summary>
    public static string ForPhoto(Guid photoId) => $"/api/v1/avatars/{photoId}";
}

public static class UserMapper
{
    public static AvatarDto ToAvatarDto(Guid? photoId, string? preset) =>
        photoId is { } id
            ? new AvatarDto("photo", null, AvatarUrls.ForPhoto(id))
            : new AvatarDto("preset", preset ?? AvatarPresets.Default, null);

    public static UserDto ToDto(this User user) =>
        new(user.Id, user.DisplayName, ToAvatarDto(user.AvatarPhotoId, user.AvatarPreset), user.PhoneLast4, user.CreatedAt);
}
