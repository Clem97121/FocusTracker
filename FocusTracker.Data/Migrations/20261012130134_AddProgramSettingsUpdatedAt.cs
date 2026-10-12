using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramSettingsUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SettingsUpdatedAt",
                table: "TrackedPrograms",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettingsUpdatedAt",
                table: "TrackedPrograms");
        }
    }
}
