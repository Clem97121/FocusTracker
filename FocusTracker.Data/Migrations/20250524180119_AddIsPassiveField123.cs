using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPassiveField123 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPassive",
                table: "TaskItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPassive",
                table: "TaskItems");
        }
    }
}
