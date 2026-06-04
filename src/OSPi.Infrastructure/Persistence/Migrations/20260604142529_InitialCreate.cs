using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OSPi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControllerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WaterLevelPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    StationDelaySeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    UseWeather = table.Column<bool>(type: "INTEGER", nullable: false),
                    SequentialDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    RainDelayUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LocationLatitude = table.Column<double>(type: "REAL", nullable: true),
                    LocationLongitude = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControllerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Programs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseWeather = table.Column<bool>(type: "INTEGER", nullable: false),
                    OddEven = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleType = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekdayMask = table.Column<byte>(type: "INTEGER", nullable: false),
                    IntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IntervalRemainder = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyDay = table.Column<int>(type: "INTEGER", nullable: false),
                    SingleRunDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    StartTimeType = table.Column<int>(type: "INTEGER", nullable: false),
                    RepeatCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RepeatEveryMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRangeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateRangeStartMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRangeStartDay = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRangeEndMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRangeEndDay = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Programs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HardwareBit = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Group = table.Column<int>(type: "INTEGER", nullable: false),
                    BoundToMaster1 = table.Column<bool>(type: "INTEGER", nullable: false),
                    BoundToMaster2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IgnoreRain = table.Column<bool>(type: "INTEGER", nullable: false),
                    IgnoreSensor = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProgramStartTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramStartTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramStartTimes_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MasterStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MasterIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ZoneId = table.Column<int>(type: "INTEGER", nullable: true),
                    OnAdjustSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    OffAdjustSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasterStations_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgramZoneDurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: false),
                    ZoneId = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RunOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramZoneDurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramZoneDurations_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProgramZoneDurations_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ZoneId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunLog_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunLog_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ControllerSettings",
                columns: new[] { "Id", "LocationLatitude", "LocationLongitude", "RainDelayUntil", "SequentialDefault", "StationDelaySeconds", "UseWeather", "WaterLevelPercent" },
                values: new object[] { 1, null, null, null, true, 0, true, 100 });

            migrationBuilder.InsertData(
                table: "MasterStations",
                columns: new[] { "Id", "MasterIndex", "OffAdjustSeconds", "OnAdjustSeconds", "ZoneId" },
                values: new object[,]
                {
                    { 1, 1, 0, 0, null },
                    { 2, 2, 0, 0, null }
                });

            migrationBuilder.InsertData(
                table: "Zones",
                columns: new[] { "Id", "BoundToMaster1", "BoundToMaster2", "Disabled", "Group", "HardwareBit", "IgnoreRain", "IgnoreSensor", "Name" },
                values: new object[,]
                {
                    { 1, false, false, false, 0, 0, false, false, "Zone 1" },
                    { 2, false, false, false, 0, 1, false, false, "Zone 2" },
                    { 3, false, false, false, 0, 2, false, false, "Zone 3" },
                    { 4, false, false, false, 0, 3, false, false, "Zone 4" },
                    { 5, false, false, false, 0, 4, false, false, "Zone 5" },
                    { 6, false, false, false, 0, 5, false, false, "Zone 6" },
                    { 7, false, false, false, 0, 6, false, false, "Zone 7" },
                    { 8, false, false, false, 0, 7, false, false, "Zone 8" },
                    { 9, false, false, false, 0, 8, false, false, "Zone 9" },
                    { 10, false, false, false, 0, 9, false, false, "Zone 10" },
                    { 11, false, false, false, 0, 10, false, false, "Zone 11" },
                    { 12, false, false, false, 0, 11, false, false, "Zone 12" },
                    { 13, false, false, false, 0, 12, false, false, "Zone 13" },
                    { 14, false, false, false, 0, 13, false, false, "Zone 14" },
                    { 15, false, false, false, 0, 14, false, false, "Zone 15" },
                    { 16, false, false, false, 0, 15, false, false, "Zone 16" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MasterStations_MasterIndex",
                table: "MasterStations",
                column: "MasterIndex",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterStations_ZoneId",
                table: "MasterStations",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramStartTimes_ProgramId",
                table: "ProgramStartTimes",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramZoneDurations_ProgramId_ZoneId",
                table: "ProgramZoneDurations",
                columns: new[] { "ProgramId", "ZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramZoneDurations_ZoneId",
                table: "ProgramZoneDurations",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLog_EndTime",
                table: "RunLog",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_RunLog_ProgramId",
                table: "RunLog",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLog_ZoneId",
                table: "RunLog",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Zones_HardwareBit",
                table: "Zones",
                column: "HardwareBit",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ControllerSettings");

            migrationBuilder.DropTable(
                name: "MasterStations");

            migrationBuilder.DropTable(
                name: "ProgramStartTimes");

            migrationBuilder.DropTable(
                name: "ProgramZoneDurations");

            migrationBuilder.DropTable(
                name: "RunLog");

            migrationBuilder.DropTable(
                name: "Programs");

            migrationBuilder.DropTable(
                name: "Zones");
        }
    }
}
