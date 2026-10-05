using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups", table =>
        {
            table.HasCheckConstraint("ck_groups_name_len", "char_length(name) BETWEEN 1 AND 80");
            table.HasCheckConstraint("ck_groups_invite_code", $"invite_code ~ '{InviteCodes.DatabasePattern}'");
        });

        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();

        builder.Property(group => group.Name).IsRequired().HasMaxLength(80);
        builder.Property(group => group.InviteCode).IsRequired().HasMaxLength(InviteCodes.Length).IsFixedLength();
        builder.Ignore(group => group.IsDeleted);

        // A senha é única entre todos os grupos (inclusive os excluídos: o código nunca é reaproveitado).
        builder.HasIndex(group => group.InviteCode).IsUnique().HasDatabaseName("ux_groups_invite_code");
    }
}
