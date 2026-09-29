using System;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260928120000_AddConsumeCoverageTargets")]
    public partial class AddConsumeCoverageTargets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consume_coverage_targets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    spec_name = table.Column<string>(type: "text", nullable: true),
                    spec_level = table.Column<string>(type: "text", nullable: true),
                    spec_rarity = table.Column<string>(type: "text", nullable: true),
                    item_target = table.Column<int>(type: "integer", nullable: false),
                    observation_target = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consume_coverage_targets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consume_coverage_targets_spec_name_spec_level_spec_rarity",
                table: "consume_coverage_targets",
                columns: new[] { "spec_name", "spec_level", "spec_rarity" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.Sql("INSERT INTO consume_coverage_targets (item_target, observation_target, enabled) VALUES (200, 10, true)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consume_coverage_targets");
        }
    }
}
