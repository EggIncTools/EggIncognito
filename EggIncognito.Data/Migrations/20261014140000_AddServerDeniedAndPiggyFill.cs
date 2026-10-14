using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations;

[DbContext(typeof(EggIncognitoDbContext))]
[Migration("20261014140000_AddServerDeniedAndPiggyFill")]
public partial class AddServerDeniedAndPiggyFill : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<bool>(
            name: "server_denied",
            table: "artifact_consume_observations",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<double>(
            name: "piggy_fill",
            table: "artifact_consume_observations",
            type: "double precision",
            nullable: false,
            defaultValue: 0d);

        migrationBuilder.Sql("""
            UPDATE artifact_consume_observations
            SET server_denied = true
            WHERE action = 'consume'
              AND spec_rarity IN ('RARE', 'EPIC', 'LEGENDARY')
              AND golden_eggs = 1
              AND byproducts = '[]'::jsonb
              AND jsonb_array_length(other_rewards) = 1
              AND other_rewards->0->>'rewardType' = 'GOLD'
              AND (other_rewards->0->>'amount')::double precision = 1;
            """);

        migrationBuilder.Sql("""
            UPDATE artifact_consume_observations
            SET piggy_fill = COALESCE((
                SELECT SUM((reward->>'amount')::double precision)
                FROM jsonb_array_elements(other_rewards) AS reward
                WHERE reward->>'rewardType' = 'PIGGY_FILL'
            ), 0)
            WHERE other_rewards::text LIKE '%PIGGY_FILL%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "piggy_fill",
            table: "artifact_consume_observations");

        migrationBuilder.DropColumn(
            name: "server_denied",
            table: "artifact_consume_observations");
    }
}
