using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebBase.Database.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAuthor",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAuthor",
                table: "Users");
        }
    }
}
