using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260930120000_AddProtoArchiveSourced")]
    public partial class AddProtoArchiveSourced : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "archive_sourced",
                table: "proto_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_sourced",
                table: "proto_versions");
        }
    }
}
