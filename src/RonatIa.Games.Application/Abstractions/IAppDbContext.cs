using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;
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

    DbSet<Group> Groups { get; }

    DbSet<GroupMember> GroupMembers { get; }

    DbSet<GameSession> GameSessions { get; }

    DbSet<SessionPlayer> SessionPlayers { get; }

    DbSet<GameEventRecord> GameEvents { get; }

    DbSet<ScoreEntry> ScoreEntries { get; }

    DbSet<SessionResult> SessionResults { get; }

    DatabaseFacade Database { get; }

    /// <summary>Permite descartar o estado rastreado depois de uma gravação que falhou, antes de tentar de novo.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
