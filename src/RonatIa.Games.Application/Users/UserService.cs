using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Users;

/// <summary>Perfil da própria pessoa: nome, avatar (pronto ou foto) e exclusão da conta.</summary>
public sealed class UserService(
    IAppDbContext db,
    AvatarService avatars,
    ISessionValidator sessionValidator,
    TimeProvider time)
{
    public const string DeleteConfirmation = "EXCLUIR";

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken cancellationToken) =>
        (await LoadAsync(userId, tracking: false, cancellationToken)).ToDto();

    public async Task<UserDto> UpdateMeAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(userId, tracking: true, cancellationToken);
        var now = time.GetUtcNow();
        var previousPhoto = user.AvatarPhotoId;

        if (request.DisplayName is not null)
        {
            user.Rename(request.DisplayName, now);
        }

        if (request.AvatarPreset is not null)
        {
            user.UseAvatarPreset(request.AvatarPreset, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (previousPhoto is { } photo && user.AvatarPhotoId != photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return user.ToDto();
    }

    /// <summary>Troca a foto do avatar pela enviada (a antiga é apagada).</summary>
    public async Task<UserDto> SetPhotoAsync(Guid userId, Stream image, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(userId, tracking: true, cancellationToken);
        var previousPhoto = user.AvatarPhotoId;

        var avatar = await avatars.StoreAsync(userId, image, cancellationToken);
        user.UseAvatarPhoto(avatar.Id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        if (previousPhoto is { } photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }

        return user.ToDto();
    }

    /// <summary>Remove a foto e volta para o avatar padrão.</summary>
    public async Task<UserDto> ClearPhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(userId, tracking: true, cancellationToken);
        var previousPhoto = user.AvatarPhotoId;

        if (previousPhoto is not null)
        {
            user.UseAvatarPreset(AvatarPresets.Default, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await avatars.DeleteIfUnreferencedAsync(previousPhoto.Value, cancellationToken);
        }

        return user.ToDto();
    }

    /// <summary>
    /// Exclusão da conta (LGPD): anonimiza (o número fica livre para um novo cadastro), apaga a foto e encerra todas as sessões.
    /// O histórico de partidas continua íntegro, atribuído a "Jogador removido".
    /// </summary>
    public async Task DeleteMeAsync(Guid userId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Confirmation?.Trim(), DeleteConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            throw AppException.Validation(
                "user.delete_not_confirmed",
                $"Para excluir a conta, envie a confirmação \"{DeleteConfirmation}\".",
                new Dictionary<string, string[]> { ["confirmation"] = [$"Digite {DeleteConfirmation} para confirmar."] });
        }

        var user = await LoadAsync(userId, tracking: true, cancellationToken);
        var now = time.GetUtcNow();
        var photoId = user.AvatarPhotoId;

        var sessions = await db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(AuthSession.ReasonUserDeleted, now);
        }

        user.MarkDeleted(now);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var session in sessions)
        {
            sessionValidator.Evict(session.Id);
        }

        if (photoId is { } photo)
        {
            await avatars.DeleteIfUnreferencedAsync(photo, cancellationToken);
        }
    }

    private async Task<User> LoadAsync(Guid userId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.Users : db.Users.AsNoTracking();
        return await query.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.NotFound("user.not_found", "Conta não encontrada.");
    }
}
