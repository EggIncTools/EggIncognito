using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260919120000_AddStagedProtoCorrections")]
    public partial class AddStagedProtoCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "staged_protos",
                type: "text",
                nullable: false,
                defaultValue: "version");

            migrationBuilder.AddColumn<int>(
                name: "target_id",
                table: "staged_protos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_staged_protos_kind",
                table: "staged_protos",
                column: "kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_staged_protos_kind",
                table: "staged_protos");

            migrationBuilder.DropColumn(
                name: "target_id",
                table: "staged_protos");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "staged_protos");
        }
    }
}
