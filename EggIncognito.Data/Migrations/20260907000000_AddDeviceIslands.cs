using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceIslands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_islands",
                columns: table => new
                {
                    device_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    provisioned = table.Column<bool>(type: "boolean", nullable: false),
                    egg_account_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_islands", x => new { x.device_id, x.user_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_islands_device_id",
                table: "device_islands",
                column: "device_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_islands");
        }
    }
}
