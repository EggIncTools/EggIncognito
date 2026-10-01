using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260930210000_DropStagedProtoCorrections")]
    public partial class DropStagedProtoCorrections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM staged_protos WHERE kind = 'correction'");

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

        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
