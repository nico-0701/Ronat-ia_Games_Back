using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RonatIa.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_sessions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rules_version = table.Column<int>(type: "integer", nullable: false),
                    host_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: true),
                    state_schema_version = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    last_event_seq = table.Column<int>(type: "integer", nullable: false),
                    rematch_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_sessions", x => x.id);
                    table.CheckConstraint("ck_game_sessions_state_pair", "(state IS NULL) = (state_schema_version IS NULL)");
                    table.CheckConstraint("ck_game_sessions_state_present", "status NOT IN ('in_progress','finished') OR state IS NOT NULL");
                    table.CheckConstraint("ck_game_sessions_status", "status IN ('waiting','in_progress','finished','cancelled')");
                    table.CheckConstraint("ck_game_sessions_version", "version >= 0 AND last_event_seq >= 0");
                    table.ForeignKey(
                        name: "fk_game_sessions_game_sessions_rematch_of_id",
                        column: x => x.rematch_of_id,
                        principalSchema: "app",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_game_sessions_group_members_host_member_id",
                        column: x => x.host_member_id,
                        principalSchema: "app",
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_game_sessions_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "app",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_session_players",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_no = table.Column<int>(type: "integer", nullable: true),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_session_players", x => x.id);
                    table.CheckConstraint("ck_game_session_players_left_at", "(status = 'joined') = (left_at IS NULL)");
                    table.CheckConstraint("ck_game_session_players_status", "status IN ('joined','left','removed')");
                    table.CheckConstraint("ck_game_session_players_team", "team_no IS NULL OR team_no BETWEEN 0 AND 15");
                    table.ForeignKey(
                        name: "fk_game_session_players_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "app",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_session_players_group_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "app",
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "game_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    actor_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_events", x => x.id);
                    table.CheckConstraint("ck_game_events_seq", "seq >= 1");
                    table.ForeignKey(
                        name: "fk_game_events_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "app",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_events_session_players_actor_player_id",
                        column: x => x.actor_player_id,
                        principalSchema: "app",
                        principalTable: "game_session_players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "score_entries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_no = table.Column<int>(type: "integer", nullable: true),
                    points = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    event_seq = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_score_entries", x => x.id);
                    table.CheckConstraint("ck_score_entries_target", "player_id IS NOT NULL OR team_no IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_score_entries_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "app",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_score_entries_session_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "app",
                        principalTable: "game_session_players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_results",
                schema: "app",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    team_no = table.Column<int>(type: "integer", nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    is_winner = table.Column<bool>(type: "boolean", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_results", x => new { x.session_id, x.player_id });
                    table.CheckConstraint("ck_session_results_rank", "rank >= 1");
                    table.ForeignKey(
                        name: "fk_session_results_game_session_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "app",
                        principalTable: "game_session_players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_results_game_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "app",
                        principalTable: "game_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_results_group_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "app",
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_results_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "app",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_events_actor_player_id",
                schema: "app",
                table: "game_events",
                column: "actor_player_id");

            migrationBuilder.CreateIndex(
                name: "ux_game_events_client_action",
                schema: "app",
                table: "game_events",
                columns: new[] { "session_id", "client_action_id" },
                unique: true,
                filter: "client_action_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_game_events_session_seq",
                schema: "app",
                table: "game_events",
                columns: new[] { "session_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_session_players_member_id",
                schema: "app",
                table: "game_session_players",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ux_game_session_players_session_member",
                schema: "app",
                table: "game_session_players",
                columns: new[] { "session_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_sessions_group_active",
                schema: "app",
                table: "game_sessions",
                column: "group_id",
                filter: "status IN ('waiting','in_progress')");

            migrationBuilder.CreateIndex(
                name: "ix_game_sessions_group_created",
                schema: "app",
                table: "game_sessions",
                columns: new[] { "group_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_game_sessions_host_member_id",
                schema: "app",
                table: "game_sessions",
                column: "host_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_sessions_rematch_of_id",
                schema: "app",
                table: "game_sessions",
                column: "rematch_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_score_entries_player_id",
                schema: "app",
                table: "score_entries",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_score_entries_session_id",
                schema: "app",
                table: "score_entries",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_results_member_id",
                schema: "app",
                table: "session_results",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_results_player_id",
                schema: "app",
                table: "session_results",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_results_ranking",
                schema: "app",
                table: "session_results",
                columns: new[] { "group_id", "game_id", "member_id" });

            // Segunda barreira contra a Data API do Supabase: RLS ligado e sem políticas em todas as tabelas do schema.
            migrationBuilder.EnableRowLevelSecurityOnAllTables("app");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "score_entries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "session_results",
                schema: "app");

            migrationBuilder.DropTable(
                name: "game_session_players",
                schema: "app");

            migrationBuilder.DropTable(
                name: "game_sessions",
                schema: "app");
        }
    }
}
