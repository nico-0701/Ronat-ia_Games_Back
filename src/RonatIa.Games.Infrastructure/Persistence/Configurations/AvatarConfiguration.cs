using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class AvatarConfiguration : IEntityTypeConfiguration<Avatar>
{
    public void Configure(EntityTypeBuilder<Avatar> builder)
    {
        builder.ToTable("avatars", table =>
        {
            table.HasCheckConstraint("ck_avatars_size", "octet_length(data) BETWEEN 1 AND 524288");
            table.HasCheckConstraint("ck_avatars_sha256_len", "octet_length(sha256) = 32");
        });

        builder.HasKey(avatar => avatar.Id);
        builder.Property(avatar => avatar.Id).ValueGeneratedNever();

        builder.Property(avatar => avatar.ContentType).IsRequired().HasMaxLength(40);
        builder.Property(avatar => avatar.Data).IsRequired();
        builder.Property(avatar => avatar.Sha256).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(avatar => avatar.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
