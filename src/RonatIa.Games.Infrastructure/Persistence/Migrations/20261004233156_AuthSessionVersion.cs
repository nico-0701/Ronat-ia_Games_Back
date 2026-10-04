using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RonatIa.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "app",
                table: "auth_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "auth_sessions");
        }
    }
}
