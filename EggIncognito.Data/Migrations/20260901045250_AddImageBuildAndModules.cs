using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImageBuildAndModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "build_blobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_build_blobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_modules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: true),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "image_builds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    spec = table.Column<string>(type: "text", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    log = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_builds", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_build_blobs_key",
                table: "build_blobs",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_build_blobs_sha256",
                table: "build_blobs",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "IX_device_modules_name",
                table: "device_modules",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_modules_sha256",
                table: "device_modules",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "IX_image_builds_state",
                table: "image_builds",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_image_builds_tag",
                table: "image_builds",
                column: "tag");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "build_blobs");

            migrationBuilder.DropTable(
                name: "device_modules");

            migrationBuilder.DropTable(
                name: "image_builds");
        }
    }
}
