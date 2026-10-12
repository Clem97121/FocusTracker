using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityDays",
                columns: table => new
                {
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Minutes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDays", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "RemoteActivityDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Minutes = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteActivityDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteActivityDays_DeviceId_Date",
                table: "RemoteActivityDays",
                columns: new[] { "DeviceId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityDays");

            migrationBuilder.DropTable(
                name: "RemoteActivityDays");
        }
    }
}
