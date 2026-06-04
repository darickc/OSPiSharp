using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OSPi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUtcOffsetToControllerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UtcOffsetMinutes",
                table: "ControllerSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "ControllerSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "UtcOffsetMinutes",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtcOffsetMinutes",
                table: "ControllerSettings");
        }
    }
}
