using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskProgramUsageBaselineDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BaselineDate",
                table: "TaskProgramUsages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineDate",
                table: "TaskProgramUsages");
        }
    }
}
