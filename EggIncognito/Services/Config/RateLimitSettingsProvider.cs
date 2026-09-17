using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class RateLimitSettingsProvider : ISettingsProvider {
    private const string Category = "Rate limiting";

    private const string TierDefaults =
        """{"Anon":{"PermitLimit":30,"WindowSeconds":60,"SegmentsPerWindow":6},"Viewer":{"PermitLimit":120,"WindowSeconds":60,"SegmentsPerWindow":6},"Contributor":{"PermitLimit":600,"WindowSeconds":60,"SegmentsPerWindow":6},"Supporter":{"PermitLimit":1200,"WindowSeconds":60,"SegmentsPerWindow":6},"Keyed":{"PermitLimit":600,"WindowSeconds":60,"SegmentsPerWindow":6}}""";

    private const string PolicyDefaults =
        """{"Egress":{"PermitLimit":10,"WindowSeconds":60,"SegmentsPerWindow":6},"Write":{"PermitLimit":60,"WindowSeconds":60,"SegmentsPerWindow":6},"Read":{"PermitLimit":120,"WindowSeconds":60,"SegmentsPerWindow":6},"Fetch":{"PermitLimit":300,"WindowSeconds":60,"SegmentsPerWindow":6},"Data":{"PermitLimit":600,"WindowSeconds":60,"SegmentsPerWindow":6},"DataAnon":{"PermitLimit":30,"WindowSeconds":60,"SegmentsPerWindow":6}}""";

    private const string ShapeNote =
        "Object of bucket name to {PermitLimit, WindowSeconds, SegmentsPerWindow}. "
        + "Unparseable or malformed content falls back to the built-in defaults instead of failing startup.";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new(SettingKeys.RateLimitingEnabled, "RateLimiting__Enabled", "Rate limiting enabled", Category,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new(SettingKeys.RateLimitingTiers, "RateLimiting__Tiers", "Tier limits", Category,
            SettingKind.Json, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = TierDefaults, Description = ShapeNote
        },
        new(SettingKeys.RateLimitingPolicies, "RateLimiting__Policies", "Policy limits", Category,
            SettingKind.Json, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = PolicyDefaults, Description = ShapeNote
        }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
