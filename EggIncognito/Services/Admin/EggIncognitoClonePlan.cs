using EggIdentity.DbClone;

namespace EggIncognito.Services.Admin;

public static class EggIncognitoClonePlan {
    public const string TargetDatabase = "eggincognito_subprod";

    public static ClonePlan Create() => new("eggincognito", TargetDatabase, [
        new TablePolicy("docs", ClonePolicy.Full) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("doc_images", ClonePolicy.Full) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("subject_tags", ClonePolicy.Full),
        new TablePolicy("tags", ClonePolicy.Full),
        new TablePolicy("game_data_documents", ClonePolicy.Full),
        new TablePolicy("game_events", ClonePolicy.Full),
        new TablePolicy("known_versions", ClonePolicy.Full),
        new TablePolicy("periodicals_snapshots", ClonePolicy.Full),
        new TablePolicy("contract_releases", ClonePolicy.Full),
        new TablePolicy("proto_protos", ClonePolicy.Full),
        new TablePolicy("proto_canonicals", ClonePolicy.Full),
        new TablePolicy("proto_versions", ClonePolicy.Full),
        new TablePolicy("proto_sha_orders", ClonePolicy.Full),
        new TablePolicy("staged_protos", ClonePolicy.Full),
        new TablePolicy("stored_endpoints", ClonePolicy.Full) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("stored_routes", ClonePolicy.Full) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("route_overrides", ClonePolicy.Full) { UserIdColumns = ["updated_by"] },
        new TablePolicy("route_binary_catalog", ClonePolicy.Full),
        new TablePolicy("env_designs", ClonePolicy.Full) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("env_design_versions", ClonePolicy.Full) { UserIdColumns = ["author_user_id"] },
        new TablePolicy("artifact_consume_observations", ClonePolicy.Full),
        new TablePolicy("site_theme_policy", ClonePolicy.Full) { UserIdColumns = ["updated_by_user_id"] },

        new TablePolicy("contributed_captures", ClonePolicy.Scrub) {
            ScrubSql =
                "UPDATE contributed_captures SET contributor_user_id = '00000000-0000-0000-0000-000000000000', "
                + "reviewed_by = NULL WHERE contributor_user_id <> '00000000-0000-0000-0000-000000000000' "
                + "OR reviewed_by IS NOT NULL",
            VerifySql =
                "SELECT 1 FROM contributed_captures "
                + "WHERE contributor_user_id <> '00000000-0000-0000-0000-000000000000' OR reviewed_by IS NOT NULL",
        },

        new TablePolicy("user_themes", ClonePolicy.Skip) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("app_settings", ClonePolicy.Skip),
        new TablePolicy("app_setting_collections", ClonePolicy.Skip),

        new TablePolicy("api_keys", ClonePolicy.SchemaOnly) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("capture_user_cas", ClonePolicy.SchemaOnly) { UserIdColumns = ["user_id"] },
        new TablePolicy("capture_proxy_addrs", ClonePolicy.SchemaOnly) { UserIdColumns = ["user_id"] },
        new TablePolicy("feed_subscriptions", ClonePolicy.SchemaOnly) { UserIdColumns = ["owner_user_id"] },
        new TablePolicy("feed_deliveries", ClonePolicy.SchemaOnly),
        new TablePolicy("feed_suppressions", ClonePolicy.SchemaOnly),
        new TablePolicy("stored_apks", ClonePolicy.SchemaOnly),
        new TablePolicy("stored_binaries", ClonePolicy.SchemaOnly),
        new TablePolicy("symbolized_binaries", ClonePolicy.SchemaOnly),
        new TablePolicy("build_blobs", ClonePolicy.SchemaOnly),
        new TablePolicy("image_builds", ClonePolicy.SchemaOnly),
        new TablePolicy("analyzed_files", ClonePolicy.SchemaOnly),
        new TablePolicy("devices", ClonePolicy.SchemaOnly),
        new TablePolicy("device_state", ClonePolicy.SchemaOnly),
        new TablePolicy("device_assets", ClonePolicy.SchemaOnly),
        new TablePolicy("device_islands", ClonePolicy.SchemaOnly),
        new TablePolicy("device_jobs", ClonePolicy.SchemaOnly),
        new TablePolicy("device_job_lines", ClonePolicy.SchemaOnly),
        new TablePolicy("device_modules", ClonePolicy.SchemaOnly),
        new TablePolicy("provisioned_instances", ClonePolicy.SchemaOnly),
        new TablePolicy("extract_jobs", ClonePolicy.SchemaOnly),
        new TablePolicy("backfill_jobs", ClonePolicy.SchemaOnly),
        new TablePolicy("DataProtectionKeys", ClonePolicy.SchemaOnly),
        new TablePolicy("deploy_state", ClonePolicy.SchemaOnly),
        new TablePolicy("bot_channel_config", ClonePolicy.SchemaOnly),
        new TablePolicy("bot_channel_state", ClonePolicy.SchemaOnly),
        new TablePolicy("site_visits_daily", ClonePolicy.SchemaOnly),
        new TablePolicy("site_visit_paths_daily", ClonePolicy.SchemaOnly),
    ]) {
        IgnoredTables = [
            "__EFMigrationsHistory",
            "eggidentity_migrations",
            "eggidentity_settings_migrations",
            "eggidentity_visits_migrations",
        ],
    };
}
