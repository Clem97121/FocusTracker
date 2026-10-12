using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "TaskItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncUpdatedAt",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Skills",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncUpdatedAt",
                table: "Skills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "SkillCategories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncUpdatedAt",
                table: "SkillCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Restrictions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncUpdatedAt",
                table: "Restrictions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "ExperienceHistories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncUpdatedAt",
                table: "ExperienceHistories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncTombstones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SyncId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncTombstones", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncTombstones");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "SyncUpdatedAt",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "SyncUpdatedAt",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "SkillCategories");

            migrationBuilder.DropColumn(
                name: "SyncUpdatedAt",
                table: "SkillCategories");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Restrictions");

            migrationBuilder.DropColumn(
                name: "SyncUpdatedAt",
                table: "Restrictions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "ExperienceHistories");

            migrationBuilder.DropColumn(
                name: "SyncUpdatedAt",
                table: "ExperienceHistories");
        }
    }
}
