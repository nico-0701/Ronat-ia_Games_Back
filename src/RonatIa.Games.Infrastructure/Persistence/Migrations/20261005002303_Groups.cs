using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RonatIa.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "groups",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    invite_code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    invite_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.CheckConstraint("ck_groups_invite_code", "invite_code ~ '^[A-HJ-KM-NP-TV-Z2-9]{8}$'");
                    table.CheckConstraint("ck_groups_name_len", "char_length(name) BETWEEN 1 AND 80");
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    avatar_preset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    avatar_photo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_members", x => x.id);
                    table.CheckConstraint("ck_group_members_left_at", "(status = 'active') = (left_at IS NULL)");
                    table.CheckConstraint("ck_group_members_owner_has_account", "role <> 'owner' OR user_id IS NOT NULL");
                    table.CheckConstraint("ck_group_members_profile_fields", "(user_id IS NOT NULL AND display_name IS NULL AND avatar_preset IS NULL AND avatar_photo_id IS NULL) OR (user_id IS NULL AND display_name IS NOT NULL AND ((avatar_preset IS NOT NULL) <> (avatar_photo_id IS NOT NULL)))");
                    table.CheckConstraint("ck_group_members_profile_is_member", "user_id IS NOT NULL OR role = 'member'");
                    table.CheckConstraint("ck_group_members_role", "role IN ('member','admin','owner')");
                    table.CheckConstraint("ck_group_members_status", "status IN ('active','left','removed')");
                    table.ForeignKey(
                        name: "fk_group_members_avatars_avatar_photo_id",
                        column: x => x.avatar_photo_id,
                        principalSchema: "app",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_group_members_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "app",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_members_avatar_photo_id",
                schema: "app",
                table: "group_members",
                column: "avatar_photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_group_id",
                schema: "app",
                table: "group_members",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_user_active",
                schema: "app",
                table: "group_members",
                column: "user_id",
                filter: "status = 'active' AND user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_group_members_group_user",
                schema: "app",
                table: "group_members",
                columns: new[] { "group_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_group_members_one_active_owner",
                schema: "app",
                table: "group_members",
                column: "group_id",
                unique: true,
                filter: "role = 'owner' AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ux_groups_invite_code",
                schema: "app",
                table: "groups",
                column: "invite_code",
                unique: true);

            // Segunda barreira contra a Data API do Supabase: RLS ligado e sem políticas em todas as tabelas do schema.
            migrationBuilder.EnableRowLevelSecurityOnAllTables("app");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_members",
                schema: "app");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "app");
        }
    }
}
