using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Application.Abstractions;

/// <summary>
/// Acesso ao banco para os casos de uso. O próprio <see cref="DbContext"/> já é a unidade de trabalho, então não há
/// repositórios genéricos por cima dele; a implementação (EF Core + Npgsql) vive na camada de Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Avatar> Avatars { get; }

    DbSet<AuthSession> AuthSessions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
