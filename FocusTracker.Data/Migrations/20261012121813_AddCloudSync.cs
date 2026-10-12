using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "HourlyAppUsageLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "RemoteDevices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteDevices", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "RemoteHourlyUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppName = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Hour = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ActiveTime = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteHourlyUsages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HourlyAppUsageLogs_UpdatedAt",
                table: "HourlyAppUsageLogs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteHourlyUsages_Date",
                table: "RemoteHourlyUsages",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteHourlyUsages_DeviceId_AppName_Date_Hour",
                table: "RemoteHourlyUsages",
                columns: new[] { "DeviceId", "AppName", "Date", "Hour" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemoteDevices");

            migrationBuilder.DropTable(
                name: "RemoteHourlyUsages");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropIndex(
                name: "IX_HourlyAppUsageLogs_UpdatedAt",
                table: "HourlyAppUsageLogs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "HourlyAppUsageLogs");
        }
    }
}
