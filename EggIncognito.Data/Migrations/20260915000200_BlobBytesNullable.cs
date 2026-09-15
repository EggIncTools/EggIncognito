using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260915000200_BlobBytesNullable")]
    public partial class BlobBytesNullable : Migration
    {
        private static readonly string[] BlobTables = [
            "device_assets", "build_blobs", "stored_binaries", "stored_apks", "device_modules"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema only. The ~660 MB of content is moved by BlobOffloadService after boot, one row at a time,
            // because a single-transaction move would hold a transaction open for minutes at startup and roll the
            // whole thing back on interrupt. NULL here means "the bytes live on disk, addressed by sha256".
            foreach (var table in BlobTables)
                migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN bytes DROP NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring NOT NULL would fail against any row already offloaded to disk, so this reverts the schema
            // only for a database where nothing has moved yet. The bytes columns are deliberately never dropped:
            // while they exist, reverting the feature is a code change rather than a data recovery.
            foreach (var table in BlobTables)
                migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN bytes SET NOT NULL;");
        }
    }
}
