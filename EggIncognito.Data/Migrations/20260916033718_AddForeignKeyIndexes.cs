using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignKeyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // proto_protos.proto_version_id is both the primary key and the foreign key to proto_versions, so the
            // value always comes from the parent row and is never generated here.
            migrationBuilder.AlterColumn<int>(
                name: "proto_version_id",
                table: "proto_protos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // Postgres indexes the referenced side of a foreign key, never the referencing side. Without these a
            // delete on the parent scans the child table once per row to enforce the constraint.
            migrationBuilder.CreateIndex(
                name: "IX_subject_tags_tag_id",
                table: "subject_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_stored_apks_source_device_id",
                table: "stored_apks",
                column: "source_device_id");

            migrationBuilder.CreateIndex(
                name: "IX_site_theme_policy_default_theme_id",
                table: "site_theme_policy",
                column: "default_theme_id");

            migrationBuilder.CreateIndex(
                name: "IX_proto_versions_canonical_id",
                table: "proto_versions",
                column: "canonical_id");

            migrationBuilder.CreateIndex(
                name: "IX_artifact_consume_observations_device_id",
                table: "artifact_consume_observations",
                column: "device_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_artifact_consume_observations_device_id",
                table: "artifact_consume_observations");

            migrationBuilder.DropIndex(
                name: "IX_proto_versions_canonical_id",
                table: "proto_versions");

            migrationBuilder.DropIndex(
                name: "IX_site_theme_policy_default_theme_id",
                table: "site_theme_policy");

            migrationBuilder.DropIndex(
                name: "IX_stored_apks_source_device_id",
                table: "stored_apks");

            migrationBuilder.DropIndex(
                name: "IX_subject_tags_tag_id",
                table: "subject_tags");

            migrationBuilder.AlterColumn<int>(
                name: "proto_version_id",
                table: "proto_protos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
