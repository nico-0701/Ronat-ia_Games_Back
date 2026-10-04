using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions", table =>
        {
            table.HasCheckConstraint("ck_auth_sessions_token_hash_len", "octet_length(token_hash) = 32");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.TokenHash).IsRequired();
        builder.Property(session => session.DeviceLabel).HasMaxLength(100);
        builder.Property(session => session.RevokedReason).HasMaxLength(30);

        builder.HasIndex(session => session.TokenHash).IsUnique().HasDatabaseName("ux_auth_sessions_token_hash");
        builder.HasIndex(session => session.PreviousTokenHash)
            .HasDatabaseName("ix_auth_sessions_previous_token_hash")
            .HasFilter("previous_token_hash IS NOT NULL");
        builder.HasIndex(session => session.UserId);
        builder.HasIndex(session => session.ExpiresAt);

        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
