using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260915000000_DropRemainingDeadTables")]
    public partial class DropRemainingDeadTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pending_auth is the last EggLedger-shaped auth table left in EggIncognito's database. 0 rows here, no
            // entity, no DbSet, no code reference, and never created by an EggIncognito migration. It also held a
            // plaintext encryption_key column, so removing it retires a secret-at-rest surface.
            migrationBuilder.Sql("DROP TABLE IF EXISTS pending_auth CASCADE;");

            // Second ASP.NET data-protection key ring. EggIncognito's live ring is the EF-mapped "DataProtectionKeys"
            // table: EggIncognitoDbContext implements IDataProtectionKeyContext and DataServices calls
            // AddDataProtection().PersistKeysToDbContext<EggIncognitoDbContext>(). The snake_case data_protection_keys
            // table is EggLedger's shape, created by its own SQL migration, and nothing in this repo reads or writes
            // it. The ring itself stays: antiforgery and Blazor Server circuit state both depend on it.
            migrationBuilder.Sql("DROP TABLE IF EXISTS data_protection_keys CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: neither table was created by an EggIncognito migration, so there is no schema to restore.
        }
    }
}
