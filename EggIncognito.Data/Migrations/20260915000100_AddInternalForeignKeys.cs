using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EggIncognitoDbContext))]
    [Migration("20260915000100_AddInternalForeignKeys")]
    public partial class AddInternalForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Foreign keys where BOTH sides live in this database. Columns naming a person (owner_user_id,
            // contributor_user_id, updated_by_user_id) are deliberately excluded: users lives in the EggIdentity
            // database, so no constraint is possible.
            //
            // The device_* -> devices family is deliberately NOT in this migration. device_jobs alone is 11k rows
            // against a 2-row devices table and DeviceStatusStore.RemoveAsync deletes a device without touching
            // children, so orphans are expected and must be reconciled before a constraint can validate.

            // FeedSubscriptionStore.AdminDeleteAsync uses ExecuteDeleteAsync, which bypasses EF and leaves delivery
            // and suppression rows behind. Any such orphan is unreachable: every read path filters by an existing
            // subscription id. Remove them first or the constraint cannot validate.
            migrationBuilder.Sql(@"
                DELETE FROM feed_deliveries d
                WHERE NOT EXISTS (SELECT 1 FROM feed_subscriptions s WHERE s.id = d.subscription_id);");
            migrationBuilder.Sql(@"
                DELETE FROM feed_suppressions f
                WHERE NOT EXISTS (SELECT 1 FROM feed_subscriptions s WHERE s.id = f.subscription_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_feed_deliveries_subscription",
                table: "feed_deliveries",
                column: "subscription_id",
                principalTable: "feed_subscriptions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_feed_suppressions_subscription",
                table: "feed_suppressions",
                column: "subscription_id",
                principalTable: "feed_subscriptions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // DocsController.SetSubjectTagsAsync already intersects the requested ids against tags, so no orphan can
            // exist. The table is empty regardless.
            migrationBuilder.AddForeignKey(
                name: "fk_subject_tags_tag",
                table: "subject_tags",
                column: "tag_id",
                principalTable: "tags",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // proto_protos.proto_version_id is both the primary key and the reference, so the relationship is 1:1 and
            // every row was created by UpsertProtoProtoAsync against a live proto_versions row.
            migrationBuilder.AddForeignKey(
                name: "fk_proto_protos_version",
                table: "proto_protos",
                column: "proto_version_id",
                principalTable: "proto_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Self-reference: a duplicate proto version points at the version it was merged into. SET NULL, not
            // cascade: deleting a canonical must not delete the duplicates that named it, it must un-merge them,
            // which is the same state MergeAsync and RestoreAsync produce when they clear canonical_id.
            migrationBuilder.Sql(@"
                UPDATE proto_versions p SET canonical_id = NULL
                WHERE p.canonical_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM proto_versions c WHERE c.id = p.canonical_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_proto_versions_canonical",
                table: "proto_versions",
                column: "canonical_id",
                principalTable: "proto_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // SET NULL: deleting the theme a site points at must clear the pointer, not delete the policy row. The
            // fallback is already handled, ThemeResolver returns null when DefaultThemeId is null.
            migrationBuilder.Sql(@"
                UPDATE site_theme_policy p SET default_theme_id = NULL
                WHERE p.default_theme_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM user_themes t WHERE t.id = p.default_theme_id);");

            migrationBuilder.AddForeignKey(
                name: "fk_site_theme_policy_default_theme",
                table: "site_theme_policy",
                column: "default_theme_id",
                principalTable: "user_themes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("fk_site_theme_policy_default_theme", "site_theme_policy");
            migrationBuilder.DropForeignKey("fk_proto_versions_canonical", "proto_versions");
            migrationBuilder.DropForeignKey("fk_proto_protos_version", "proto_protos");
            migrationBuilder.DropForeignKey("fk_subject_tags_tag", "subject_tags");
            migrationBuilder.DropForeignKey("fk_feed_suppressions_subscription", "feed_suppressions");
            migrationBuilder.DropForeignKey("fk_feed_deliveries_subscription", "feed_deliveries");
        }
    }
}
