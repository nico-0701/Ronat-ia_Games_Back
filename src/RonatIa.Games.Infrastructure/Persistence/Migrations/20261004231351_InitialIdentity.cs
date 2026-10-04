using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RonatIa.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    device_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_sessions", x => x.id);
                    table.CheckConstraint("ck_auth_sessions_token_hash_len", "octet_length(token_hash) = 32");
                });

            migrationBuilder.CreateTable(
                name: "avatars",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatars", x => x.id);
                    table.CheckConstraint("ck_avatars_sha256_len", "octet_length(sha256) = 32");
                    table.CheckConstraint("ck_avatars_size", "octet_length(data) BETWEEN 1 AND 524288");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    phone_last4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    avatar_preset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    avatar_photo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terms_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_avatar_one_of", "(avatar_preset IS NOT NULL) <> (avatar_photo_id IS NOT NULL)");
                    table.CheckConstraint("ck_users_display_name_len", "char_length(display_name) BETWEEN 1 AND 60");
                    table.CheckConstraint("ck_users_phone_hash_len", "octet_length(phone_hash) = 32");
                    table.CheckConstraint("ck_users_phone_last4", "phone_last4 ~ '^[0-9]{4}$'");
                    table.CheckConstraint("ck_users_status", "status IN ('active','suspended','deleted')");
                    table.ForeignKey(
                        name: "fk_users_avatars_avatar_photo_id",
                        column: x => x.avatar_photo_id,
                        principalSchema: "app",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_expires_at",
                schema: "app",
                table: "auth_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_previous_token_hash",
                schema: "app",
                table: "auth_sessions",
                column: "previous_token_hash",
                filter: "previous_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_user_id",
                schema: "app",
                table: "auth_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_auth_sessions_token_hash",
                schema: "app",
                table: "auth_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_avatars_uploaded_by_user_id",
                schema: "app",
                table: "avatars",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_photo_id",
                schema: "app",
                table: "users",
                column: "avatar_photo_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_phone_hash",
                schema: "app",
                table: "users",
                column: "phone_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_users_user_id",
                schema: "app",
                table: "auth_sessions",
                column: "user_id",
                principalSchema: "app",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_avatars_users_uploaded_by_user_id",
                schema: "app",
                table: "avatars",
                column: "uploaded_by_user_id",
                principalSchema: "app",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Segunda barreira contra a Data API do Supabase: RLS ligado e sem políticas em todas as tabelas do schema.
            migrationBuilder.EnableRowLevelSecurityOnAllTables("app");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_avatars_users_uploaded_by_user_id",
                schema: "app",
                table: "avatars");

            migrationBuilder.DropTable(
                name: "auth_sessions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "avatars",
                schema: "app");
        }
    }
}
