using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    public partial class DropVirtualDeviceTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "provisioned_instances");
            migrationBuilder.DropTable(name: "image_builds");
            migrationBuilder.DropTable(name: "device_modules");
            migrationBuilder.DropTable(name: "build_blobs");
            migrationBuilder.DropTable(name: "extract_jobs");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extract_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    platform = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_extract_jobs", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_extract_jobs_platform_app_version",
                table: "extract_jobs",
                columns: new[] { "platform", "app_version" },
                unique: true);

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
                    bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table => table.PrimaryKey("PK_build_blobs", x => x.id));

            migrationBuilder.CreateIndex(name: "IX_build_blobs_key", table: "build_blobs", column: "key", unique: true);
            migrationBuilder.CreateIndex(name: "IX_build_blobs_sha256", table: "build_blobs", column: "sha256");

            migrationBuilder.CreateTable(
                name: "device_modules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table => table.PrimaryKey("PK_device_modules", x => x.id));

            migrationBuilder.CreateIndex(name: "IX_device_modules_name", table: "device_modules", column: "name", unique: true);
            migrationBuilder.CreateIndex(name: "IX_device_modules_sha256", table: "device_modules", column: "sha256");

            migrationBuilder.CreateTable(
                name: "image_builds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tag = table.Column<string>(type: "text", nullable: false),
                    spec = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    log = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    note = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_image_builds", x => x.id));

            migrationBuilder.CreateIndex(name: "IX_image_builds_state", table: "image_builds", column: "state");
            migrationBuilder.CreateIndex(name: "IX_image_builds_tag", table: "image_builds", column: "tag");

            migrationBuilder.CreateTable(
                name: "provisioned_instances",
                columns: table => new
                {
                    instance_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    image = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    adb_serial = table.Column<string>(type: "text", nullable: true),
                    host_ref = table.Column<string>(type: "text", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioned_instances", x => x.instance_id);
                    table.ForeignKey(
                        name: "fk_provisioned_instances_device",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(name: "IX_provisioned_instances_device_id", table: "provisioned_instances", column: "device_id");
            migrationBuilder.CreateIndex(name: "IX_provisioned_instances_state", table: "provisioned_instances", column: "state");
        }
    }
}
