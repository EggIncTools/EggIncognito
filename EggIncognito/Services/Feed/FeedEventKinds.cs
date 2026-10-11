using EggIncognito.Services.Feed.Kinds;

namespace EggIncognito.Services.Feed;

public static class FeedEventKinds {
    public const string ProtoBuild = "proto_build";
    public const string ConfigChanged = "config_changed";
    public const string GameDataRebuilt = "gamedata_rebuilt";
    public const string GameEvent = "game_event";
    public const string ContractRelease = "contract_release";

    public const string LegacyPeriodicalsChanged = "periodicals_changed";

    public const string TriggerVersionUp = "version_up";
    public const string TriggerProtoChanged = "proto_changed";
    public const string TriggerNewVersion = "new_version";
    public const string TriggerSuspect = "suspect";

    public const string TriggerAnyFeed = "any";

    public const string TriggerAnyRebuild = "any_rebuild";
    public const string TriggerBinaryUp = "binary_up";

    public const string TriggerAny = "any";
    public const string TriggerUltraOnly = "ultra_only";
    public const string TriggerStandardOnly = "standard_only";
    public const string TriggerNewOnly = "new_only";
    public const string TriggerLeggacyOnly = "leggacy_only";

    public const string FilterRequireClientVersion = "require_client_version";
    public const string FilterRequireProto = "require_proto";
    public const string FilterSaneBuild = "sane_build";
    public const string FilterKnownDelta = "known_delta";
    public const string FilterRequireAspects = "require_aspects";
    public const string FilterRequireIds = "require_ids";
    public const string FilterFreshOnly = "fresh_only";
    public const string FilterDeviceOnly = "device_only";

    public static readonly TimeSpan FreshWindow = TimeSpan.FromHours(48);

    public static NotificationKind Proto => ProtoBuildKind.Definition;
    public static NotificationKind Config => ConfigChangedKind.Definition;
    public static NotificationKind GameData => GameDataRebuiltKind.Definition;
    public static NotificationKind Events => GameEventKind.Definition;
    public static NotificationKind Contracts => ContractReleaseKind.Definition;

    public static readonly IReadOnlyList<NotificationKind> All = [Proto, Config, GameData, Events, Contracts];

    public static NotificationKind? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static NotificationKind For(INotificationEvent evt) =>
        Find(evt.Kind) ?? throw new InvalidOperationException($"unknown notification kind {evt.Kind}");

    public static bool IsValid(string? key) => Find(key) is not null;

    public static string Normalize(string? key) =>
        string.Equals(key, LegacyPeriodicalsChanged, StringComparison.Ordinal)
            ? ConfigChanged
            : Find(key)?.Key ?? ProtoBuild;

    public static string NormalizeTrigger(string kind, string? trigger) {
        var info = Find(kind) ?? Proto;
        return info.Trigger(trigger)?.Value ?? info.DefaultTrigger;
    }

    public static string[] NormalizeFilters(string kind, IEnumerable<string>? filters) {
        var info = Find(kind) ?? Proto;
        return filters is null
            ? [.. info.Filters.Where(f => f.DefaultOn).Select(f => f.Key)]
            : [.. info.Filters.Where(f => filters.Contains(f.Key, StringComparer.Ordinal)).Select(f => f.Key)];
    }

    public static IReadOnlyList<FeedSample> Samples(string? kind) => Find(kind)?.Samples ?? [];

    public static FeedSample? Sample(string? kind, string? key) =>
        Samples(kind).FirstOrDefault(s => s.Key == key);
}
