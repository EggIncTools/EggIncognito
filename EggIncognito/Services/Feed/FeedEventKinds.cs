using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Feed;

public sealed record FeedTriggerOption(string Value, string Label);

public sealed record FeedFilterOption(string Key, string Label, bool DefaultOn);

public sealed record FeedEventKindInfo(
    string Key,
    string Label,
    IReadOnlyList<FeedTriggerOption> Triggers,
    IReadOnlyList<string> Vars,
    string DefaultTrigger,
    bool PlatformScoped,
    IReadOnlyList<FeedFilterOption> Filters);

public static class FeedEventKinds {
    public const string ProtoBuild = "proto_build";
    public const string ConfigChanged = "config_changed";
    public const string GameDataRebuilt = "gamedata_rebuilt";

    public const string LegacyPeriodicalsChanged = "periodicals_changed";

    public const string TriggerVersionUp = "version_up";
    public const string TriggerProtoChanged = "proto_changed";
    public const string TriggerNewVersion = "new_version";
    public const string TriggerSuspect = "suspect";

    public const string TriggerAnyFeed = "any";

    public const string TriggerAnyRebuild = "any_rebuild";
    public const string TriggerBinaryUp = "binary_up";

    public const string FilterRequireClientVersion = "require_client_version";
    public const string FilterRequireProto = "require_proto";
    public const string FilterSaneBuild = "sane_build";
    public const string FilterKnownDelta = "known_delta";
    public const string FilterRequireAspects = "require_aspects";
    public const string FilterRequireIds = "require_ids";

    public static readonly FeedEventKindInfo Proto = new(
        ProtoBuild, "Proto build",
        [
            new FeedTriggerOption(TriggerVersionUp, "New version"),
            new FeedTriggerOption(TriggerProtoChanged, "New version, proto changed"),
            new FeedTriggerOption(TriggerNewVersion, "New version, first insert"),
            new FeedTriggerOption(TriggerSuspect, "New version, suspect extraction")
        ],
        [
            "platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl",
            "delta", "prevAppVersion", "prevBuild", "flaws"
        ],
        TriggerVersionUp, true,
        [
            new FeedFilterOption(FilterRequireClientVersion, "Require client version", true),
            new FeedFilterOption(FilterRequireProto, "Require proto", true),
            new FeedFilterOption(FilterSaneBuild, "Require sane build", true),
            new FeedFilterOption(FilterKnownDelta, "Require known delta", true)
        ]);

    public static readonly FeedEventKindInfo Config = new(
        ConfigChanged, "Config changed",
        [
            new FeedTriggerOption(TriggerAnyFeed, "Any response"),
            .. ConfigFeeds.All.Select(f => new FeedTriggerOption(f.Id, f.Label))
        ],
        ["feed", "feedLabel", "sha", "pageUrl", "changed", "added", "removed"],
        TriggerAnyFeed, false,
        [
            new FeedFilterOption(FilterRequireAspects, "Require identified change", true),
            new FeedFilterOption(FilterRequireIds, "Require added or removed entries", false)
        ]);

    public static readonly FeedEventKindInfo GameData = new(
        GameDataRebuilt, "Game data rebuilt",
        [
            new FeedTriggerOption(TriggerAnyRebuild, "Any document changed"),
            new FeedTriggerOption(TriggerBinaryUp, "New binary version")
        ],
        ["binaryVersion", "prevBinaryVersion", "platform", "changedDocs", "docCount", "inputSha", "pageUrl"],
        TriggerAnyRebuild, false,
        []);

    public static readonly IReadOnlyList<FeedEventKindInfo> All = [Proto, Config, GameData];

    public static FeedEventKindInfo? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static bool IsValid(string? key) => Find(key) is not null;

    public static string Normalize(string? key) =>
        string.Equals(key, LegacyPeriodicalsChanged, StringComparison.Ordinal)
            ? ConfigChanged
            : IsValid(key) ? key! : ProtoBuild;

    public static string NormalizeTrigger(string kind, string? trigger) {
        var info = Find(kind) ?? Proto;
        return info.Triggers.Any(t => t.Value == trigger) ? trigger! : info.DefaultTrigger;
    }

    public static string[] NormalizeFilters(string kind, IEnumerable<string>? filters) {
        var info = Find(kind) ?? Proto;
        return filters is null
            ? [.. info.Filters.Where(f => f.DefaultOn).Select(f => f.Key)]
            : [.. info.Filters.Where(f => filters.Contains(f.Key, StringComparer.Ordinal)).Select(f => f.Key)];
    }
}
