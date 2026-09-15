using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260915000300_AddDeviceForeignKeys")]
    public partial class AddDeviceForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every orphan here belonged to a destroyed redroid virtual device (RedroidProvisioner.NamePrefix,
            // "egi-vd-"). VirtualDeviceLifecycle.DestroyAsync removes the devices row and nothing removed the job
            // history, so each destroyed container left its rows behind. The container is gone and its random id can
            // never recur, so these are logs for machines that cannot come back.
            // device_job_lines already has a cascading FK to device_jobs, so the lines go with the jobs.
            migrationBuilder.Sql(@"
                DELETE FROM device_jobs x
                WHERE NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.device_id);");
            migrationBuilder.Sql(@"
                DELETE FROM device_state x
                WHERE NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.device_id);");
            migrationBuilder.Sql(@"
                DELETE FROM device_islands x
                WHERE NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.device_id);");

            // Nullable pointers are provenance, not parentage: null the pointer and keep the row. An artifact
            // observation is real game data and a stored APK is a real artifact; neither stops being true because
            // the device that produced it was destroyed.
            migrationBuilder.Sql(@"
                UPDATE artifact_consume_observations x SET device_id = NULL
                WHERE x.device_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.device_id);");
            migrationBuilder.Sql(@"
                UPDATE stored_apks x SET source_device_id = NULL
                WHERE x.source_device_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.source_device_id);");
            migrationBuilder.Sql(@"
                UPDATE provisioned_instances x SET device_id = NULL
                WHERE x.device_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM devices d WHERE d.id = x.device_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_device_jobs_device",
                table: "device_jobs",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_device_state_device",
                table: "device_state",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_device_islands_device",
                table: "device_islands",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_artifact_consume_observations_device",
                table: "artifact_consume_observations",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_provisioned_instances_device",
                table: "provisioned_instances",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_stored_apks_source_device",
                table: "stored_apks",
                column: "source_device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("fk_stored_apks_source_device", "stored_apks");
            migrationBuilder.DropForeignKey("fk_provisioned_instances_device", "provisioned_instances");
            migrationBuilder.DropForeignKey("fk_artifact_consume_observations_device",
                "artifact_consume_observations");
            migrationBuilder.DropForeignKey("fk_device_islands_device", "device_islands");
            migrationBuilder.DropForeignKey("fk_device_state_device", "device_state");
            migrationBuilder.DropForeignKey("fk_device_jobs_device", "device_jobs");
        }
    }
}
