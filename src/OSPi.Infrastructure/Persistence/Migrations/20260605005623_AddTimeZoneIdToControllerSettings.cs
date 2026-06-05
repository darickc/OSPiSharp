using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OSPi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeZoneIdToControllerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "ControllerSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ControllerSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "TimeZoneId",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "ControllerSettings");
        }
    }
}
