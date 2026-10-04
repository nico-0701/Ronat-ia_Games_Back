using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_phone_hash_len", "octet_length(phone_hash) = 32");
            table.HasCheckConstraint("ck_users_phone_last4", "phone_last4 ~ '^[0-9]{4}$'");
            table.HasCheckConstraint("ck_users_display_name_len", "char_length(display_name) BETWEEN 1 AND 60");
            table.HasCheckConstraint("ck_users_avatar_one_of", "(avatar_preset IS NOT NULL) <> (avatar_photo_id IS NOT NULL)");
            table.HasCheckConstraint("ck_users_status", "status IN ('active','suspended','deleted')");
        });

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.PhoneHash).IsRequired();
        builder.Property(user => user.PhoneLast4).IsRequired().HasMaxLength(4).IsFixedLength();
        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(60);
        builder.Property(user => user.AvatarPreset).HasMaxLength(20);
        builder.Property(user => user.Status).HasConversion<SnakeCaseEnumConverter<UserStatus>>().IsRequired().HasMaxLength(20);
        builder.Property(user => user.TermsVersion).HasMaxLength(20);

        builder.HasIndex(user => user.PhoneHash).IsUnique().HasDatabaseName("ux_users_phone_hash");

        builder.HasOne<Avatar>().WithMany().HasForeignKey(user => user.AvatarPhotoId).OnDelete(DeleteBehavior.Restrict);
    }
}
