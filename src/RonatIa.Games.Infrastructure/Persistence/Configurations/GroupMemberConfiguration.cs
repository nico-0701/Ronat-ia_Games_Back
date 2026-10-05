using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RonatIa.Games.Domain.Groups;
using RonatIa.Games.Domain.Users;

namespace RonatIa.Games.Infrastructure.Persistence.Configurations;

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members", table =>
        {
            table.HasCheckConstraint("ck_group_members_role", "role IN ('member','admin','owner')");
            table.HasCheckConstraint("ck_group_members_status", "status IN ('active','left','removed')");
            table.HasCheckConstraint("ck_group_members_owner_has_account", "role <> 'owner' OR user_id IS NOT NULL");
            table.HasCheckConstraint("ck_group_members_profile_is_member", "user_id IS NOT NULL OR role = 'member'");

            // Quem tem conta usa o nome e o avatar da conta; o perfil sem conta tem nome e exatamente um avatar (pronto ou foto).
            table.HasCheckConstraint(
                "ck_group_members_profile_fields",
                "(user_id IS NOT NULL AND display_name IS NULL AND avatar_preset IS NULL AND avatar_photo_id IS NULL) " +
                "OR (user_id IS NULL AND display_name IS NOT NULL AND ((avatar_preset IS NOT NULL) <> (avatar_photo_id IS NOT NULL)))");

            table.HasCheckConstraint("ck_group_members_left_at", "(status = 'active') = (left_at IS NULL)");
        });

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();

        // Concorrência otimista (mesmo padrão de auth_sessions): a segunda de duas reivindicações simultâneas falha.
        builder.Property(member => member.Version).IsConcurrencyToken();

        builder.Property(member => member.DisplayName).HasMaxLength(60);
        builder.Property(member => member.AvatarPreset).HasMaxLength(20);
        builder.Property(member => member.Role).HasConversion<SnakeCaseEnumConverter<GroupRole>>().IsRequired().HasMaxLength(20);
        builder.Property(member => member.Status).HasConversion<SnakeCaseEnumConverter<MemberStatus>>().IsRequired().HasMaxLength(20);
        builder.Ignore(member => member.HasAccount);
        builder.Ignore(member => member.IsActive);

        // Uma pessoa tem no máximo uma linha por grupo (perfis sem conta, user_id nulo, não entram na regra).
        builder.HasIndex(member => new { member.GroupId, member.UserId })
            .IsUnique()
            .HasDatabaseName("ux_group_members_group_user")
            .HasFilter("user_id IS NOT NULL");

        // Dois índices na mesma coluna (group_id) precisam de nome explícito; sem ele o EF os funde em um só.
        // No máximo um dono ativo por grupo, garantido pelo banco (a transferência rebaixa um e promove o outro em duas etapas).
        builder.HasIndex(member => member.GroupId, "ux_group_members_one_active_owner")
            .HasDatabaseName("ux_group_members_one_active_owner")
            .IsUnique()
            .HasFilter("role = 'owner' AND status = 'active'");

        // Membros de um grupo (inclui os perfis sem conta, que o índice único de (grupo, conta) não cobre).
        builder.HasIndex(member => member.GroupId, "ix_group_members_group_id").HasDatabaseName("ix_group_members_group_id");

        // "Meus grupos".
        builder.HasIndex(member => member.UserId, "ix_group_members_user_active")
            .HasDatabaseName("ix_group_members_user_active")
            .HasFilter("status = 'active' AND user_id IS NOT NULL");

        builder.HasOne<Group>().WithMany().HasForeignKey(member => member.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Avatar>().WithMany().HasForeignKey(member => member.AvatarPhotoId).OnDelete(DeleteBehavior.Restrict);
    }
}
