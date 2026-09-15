using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260913000000_DropDeadEggLedgerTables")]
    public partial class DropDeadEggLedgerTables : Migration
    {
        private static readonly string[] DeadTables = [
            "el_artifact_drops",
            "el_backup",
            "el_mission",
            "el_report_groups",
            "el_reports",
            "el_settings",
            "blobs",
            "sessions"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EggLedger-shaped tables that exist in EggIncognito's database at 0 rows and are referenced by no
            // EggIncognito code, entity or migration. They are not EggLedger's live tables, which are in EggLedger's
            // own database. `build_blobs` is EggIncognito's and is deliberately not in this list.
            foreach (var table in DeadTables)
                migrationBuilder.Sql($"DROP TABLE IF EXISTS {table} CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: these tables were never created by an EggIncognito migration, so there is no schema to restore.
        }
    }
}
