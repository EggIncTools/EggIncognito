namespace EggIncognito.Services.Config;

public static class SettingKeys {
    public const string VirtualImageOverride = "devices.virtual.image";

    public const string DeviceSyncEnabled = "device_sync.enabled";
    public const string DeviceSyncAutoPublish = "device_sync.auto_publish";
    public const string DeviceSyncRetryBackoffMinutes = "device_sync.retry_backoff_minutes";
    public const string DeviceSyncStoreProbeIntervalMinutes = "device_sync.store_probe_interval_minutes";

    public const string ThemeCustomCss = "theme.custom_css";
    public const string ApiKeysMaxPerUser = "api_keys.max_per_user";
    public const string FeedPageBaseUrl = "feed.page_base_url";

    public const string BlobOffloadEnabled = "storage.blob_offload_enabled";

    public const string RateLimitingEnabled = "rate_limiting.enabled";
    public const string RateLimitingTiers = "rate_limiting.tiers";
    public const string RateLimitingPolicies = "rate_limiting.policies";
}
