using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20261002120000_AddAnalyzedFileExtractor")]
    public partial class AddAnalyzedFileExtractor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extractor",
                table: "analyzed_files",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extractor",
                table: "analyzed_files");
        }
    }
}
