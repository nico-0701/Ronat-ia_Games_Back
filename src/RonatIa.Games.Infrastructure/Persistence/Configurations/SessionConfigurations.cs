using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Sessions;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    public void Configure(EntityTypeBuilder<GameSession> builder)
    {
        builder.ToTable("game_sessions", table =>
        {
            table.HasCheckConstraint("ck_game_sessions_status", "status IN ('waiting','in_progress','finished','cancelled')");
            table.HasCheckConstraint("ck_game_sessions_state_pair", "(state IS NULL) = (state_schema_version IS NULL)");
            table.HasCheckConstraint("ck_game_sessions_state_present", "status NOT IN ('in_progress','finished') OR state IS NOT NULL");
            table.HasCheckConstraint("ck_game_sessions_version", "version >= 0 AND last_event_seq >= 0");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        // Concorrência otimista: duas ações simultâneas sobre a mesma partida não gravam as duas.
        builder.Property(session => session.Version).IsConcurrencyToken();

        builder.Property(session => session.GameId).IsRequired().HasMaxLength(40);
        builder.Property(session => session.Status).HasConversion<SnakeCaseEnumConverter<SessionStatus>>().IsRequired().HasMaxLength(20);
        builder.Property(session => session.ConfigJson).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        builder.Property(session => session.StateJson).HasColumnName("state").HasColumnType("jsonb");
        builder.Ignore(session => session.IsActive);

        builder.HasIndex(session => new { session.GroupId, session.CreatedAt }, "ix_game_sessions_group_created")
            .HasDatabaseName("ix_game_sessions_group_created")
            .IsDescending(false, true);

        builder.HasIndex(session => session.GroupId, "ix_game_sessions_group_active")
            .HasDatabaseName("ix_game_sessions_group_active")
            .HasFilter("status IN ('waiting','in_progress')");

        builder.HasOne<Group>().WithMany().HasForeignKey(session => session.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GroupMember>().WithMany().HasForeignKey(session => session.HostMemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GameSession>().WithMany().HasForeignKey(session => session.RematchOfId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SessionPlayerConfiguration : IEntityTypeConfiguration<SessionPlayer>
{
    public void Configure(EntityTypeBuilder<SessionPlayer> builder)
    {
        builder.ToTable("game_session_players", table =>
        {
            table.HasCheckConstraint("ck_game_session_players_status", "status IN ('joined','left','removed')");
            table.HasCheckConstraint("ck_game_session_players_team", "team_no IS NULL OR team_no BETWEEN 0 AND 15");
            table.HasCheckConstraint("ck_game_session_players_left_at", "(status = 'joined') = (left_at IS NULL)");
        });

        builder.HasKey(player => player.Id);
        builder.Property(player => player.Id).ValueGeneratedNever();

        builder.Property(player => player.Status).HasConversion<SnakeCaseEnumConverter<PlayerStatus>>().IsRequired().HasMaxLength(20);
        builder.Ignore(player => player.IsActive);

        // Cada membro do grupo tem no máximo uma linha por partida (sair e voltar reativa a mesma).
        builder.HasIndex(player => new { player.SessionId, player.MemberId }).IsUnique().HasDatabaseName("ux_game_session_players_session_member");

        builder.HasOne<GameSession>().WithMany().HasForeignKey(player => player.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GroupMember>().WithMany().HasForeignKey(player => player.MemberId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GameEventRecordConfiguration : IEntityTypeConfiguration<GameEventRecord>
{
    public void Configure(EntityTypeBuilder<GameEventRecord> builder)
    {
        builder.ToTable("game_events", table =>
        {
            table.HasCheckConstraint("ck_game_events_seq", "seq >= 1");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedOnAdd();

        builder.Property(record => record.Type).IsRequired().HasMaxLength(80);
        builder.Property(record => record.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");

        builder.HasIndex(record => new { record.SessionId, record.Seq }).IsUnique().HasDatabaseName("ux_game_events_session_seq");

        // Idempotência: o mesmo clientActionId só entra uma vez por partida.
        builder.HasIndex(record => new { record.SessionId, record.ClientActionId })
            .IsUnique()
            .HasDatabaseName("ux_game_events_client_action")
            .HasFilter("client_action_id IS NOT NULL");

        builder.HasOne<GameSession>().WithMany().HasForeignKey(record => record.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SessionPlayer>().WithMany().HasForeignKey(record => record.ActorPlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScoreEntryConfiguration : IEntityTypeConfiguration<ScoreEntry>
{
    public void Configure(EntityTypeBuilder<ScoreEntry> builder)
    {
        builder.ToTable("score_entries", table =>
        {
            table.HasCheckConstraint("ck_score_entries_target", "player_id IS NOT NULL OR team_no IS NOT NULL");
        });

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedOnAdd();

        builder.Property(entry => entry.Reason).IsRequired().HasMaxLength(80);

        builder.HasIndex(entry => entry.SessionId).HasDatabaseName("ix_score_entries_session_id");

        builder.HasOne<GameSession>().WithMany().HasForeignKey(entry => entry.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SessionPlayer>().WithMany().HasForeignKey(entry => entry.PlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionResultConfiguration : IEntityTypeConfiguration<SessionResult>
{
    public void Configure(EntityTypeBuilder<SessionResult> builder)
    {
        builder.ToTable("session_results", table =>
        {
            table.HasCheckConstraint("ck_session_results_rank", "rank >= 1");
        });

        builder.HasKey(result => new { result.SessionId, result.PlayerId });

        builder.Property(result => result.GameId).IsRequired().HasMaxLength(40);

        // Base do ranking do grupo: por jogo e por membro.
        builder.HasIndex(result => new { result.GroupId, result.GameId, result.MemberId }).HasDatabaseName("ix_session_results_ranking");

        builder.HasOne<GameSession>().WithMany().HasForeignKey(result => result.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SessionPlayer>().WithMany().HasForeignKey(result => result.PlayerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GroupMember>().WithMany().HasForeignKey(result => result.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Group>().WithMany().HasForeignKey(result => result.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
