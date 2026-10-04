using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Application.Users;

/// <summary>Perfil da própria pessoa.</summary>
public sealed class UserService(IAppDbContext db)
{
    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw AppException.NotFound("user.not_found", "Conta não encontrada.");

        return user.ToDto();
    }
}
