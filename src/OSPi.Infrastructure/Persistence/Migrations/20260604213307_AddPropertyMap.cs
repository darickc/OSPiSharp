using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OSPi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    ImageHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ImageWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageHeight = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyMapId = table.Column<int>(type: "INTEGER", nullable: false),
                    ZoneId = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapMarkers_PropertyMaps_PropertyMapId",
                        column: x => x.PropertyMapId,
                        principalTable: "PropertyMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MapMarkers_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PropertyMaps",
                columns: new[] { "Id", "ImageHash", "ImageHeight", "ImagePath", "ImageWidth" },
                values: new object[] { 1, null, 0, null, 0 });

            migrationBuilder.CreateIndex(
                name: "IX_MapMarkers_PropertyMapId_ZoneId",
                table: "MapMarkers",
                columns: new[] { "PropertyMapId", "ZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MapMarkers_ZoneId",
                table: "MapMarkers",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapMarkers");

            migrationBuilder.DropTable(
                name: "PropertyMaps");
        }
    }
}
