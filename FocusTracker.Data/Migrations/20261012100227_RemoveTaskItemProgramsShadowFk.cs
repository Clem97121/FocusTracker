using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTaskItemProgramsShadowFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrackedPrograms_TaskItems_TaskItemId",
                table: "TrackedPrograms");

            migrationBuilder.DropIndex(
                name: "IX_TrackedPrograms_TaskItemId",
                table: "TrackedPrograms");

            migrationBuilder.DropColumn(
                name: "TaskItemId",
                table: "TrackedPrograms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaskItemId",
                table: "TrackedPrograms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPrograms_TaskItemId",
                table: "TrackedPrograms",
                column: "TaskItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrackedPrograms_TaskItems_TaskItemId",
                table: "TrackedPrograms",
                column: "TaskItemId",
                principalTable: "TaskItems",
                principalColumn: "Id");
        }
    }
}
