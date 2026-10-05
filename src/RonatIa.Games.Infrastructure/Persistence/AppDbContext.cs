using Microsoft.EntityFrameworkCore;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Avatar> Avatars => Set<Avatar>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<GameSession> GameSessions => Set<GameSession>();

    public DbSet<SessionPlayer> SessionPlayers => Set<SessionPlayer>();

    public DbSet<GameEventRecord> GameEvents => Set<GameEventRecord>();

    public DbSet<ScoreEntry> ScoreEntries => Set<ScoreEntry>();

    public DbSet<SessionResult> SessionResults => Set<SessionResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(PersistenceConfiguration.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
