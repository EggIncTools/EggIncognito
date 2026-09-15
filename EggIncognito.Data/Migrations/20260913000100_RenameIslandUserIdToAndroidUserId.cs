using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260913000100_RenameIslandUserIdToAndroidUserId")]
    public partial class RenameIslandUserIdToAndroidUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // device_islands.user_id is the Android multi-user profile id from `pm list users`, not a person.
            // The old name invited reconciliation with the owner_user_id uuid that identifies an EggIdentity user.
            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "device_islands",
                newName: "android_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "android_user_id",
                table: "device_islands",
                newName: "user_id");
        }
    }
}
