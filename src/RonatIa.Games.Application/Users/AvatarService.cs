using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Domain.Errors;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Users;

/// <summary>
/// Fotos de avatar: processa o arquivo enviado, guarda no próprio banco e serve a imagem. A foto fica "solta" até alguém
/// (uma conta ou, depois, um membro de grupo) apontar para ela; fotos substituídas são apagadas quando ninguém mais as usa.
/// </summary>
public sealed class AvatarService(IAppDbContext db, IAvatarImageProcessor processor, TimeProvider time)
{
    public AvatarPresetsDto Presets() => new(AvatarPresets.Default, AvatarPresets.Keys);

    /// <summary>Processa e adiciona a foto ao contexto (a gravação acontece no próximo <c>SaveChanges</c>).</summary>
    public async Task<Avatar> StoreAsync(Guid uploadedByUserId, Stream image, CancellationToken cancellationToken)
    {
        var processed = await processor.ProcessAsync(image, cancellationToken);
        var avatar = Avatar.Create(
            uploadedByUserId,
            processed.ContentType,
            processed.Data,
            processed.Width,
            processed.Height,
            processed.Sha256,
            time.GetUtcNow());

        db.Avatars.Add(avatar);
        return avatar;
    }

    public async Task<AvatarContent> GetContentAsync(Guid avatarId, CancellationToken cancellationToken)
    {
        var avatar = await db.Avatars.AsNoTracking()
            .Where(a => a.Id == avatarId)
            .Select(a => new { a.Data, a.ContentType, a.Sha256 })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("avatar.not_found", "Foto não encontrada.");

        return new AvatarContent(avatar.Data, avatar.ContentType, Convert.ToHexString(avatar.Sha256).ToLowerInvariant());
    }

    /// <summary>Apaga a foto se nenhuma conta (nem perfil de grupo sem conta) aponta para ela.</summary>
    public async Task DeleteIfUnreferencedAsync(Guid avatarId, CancellationToken cancellationToken)
    {
        var inUse = await db.Users.AnyAsync(u => u.AvatarPhotoId == avatarId, cancellationToken)
            || await db.GroupMembers.AnyAsync(m => m.AvatarPhotoId == avatarId, cancellationToken);
        if (!inUse)
        {
            await db.Avatars.Where(a => a.Id == avatarId).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
