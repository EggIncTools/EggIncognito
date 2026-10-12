using EggIdentity.Filters;
using EggIncognito.Data.Models;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record ConfigChangedEvent(
    string Feed,
    string Sha,
    string PageUrl,
    ConfigChangeSummary? Change = null,
    string? DedupSha = null) : INotificationEvent {
    public string Kind => FeedEventKinds.ConfigChanged;
    public string DedupKey => $"{Feed}:{DedupSha ?? Sha}";

    public FilterSubjectValue Subject(string field) => field switch {
        ConfigChangedKind.Feed => FilterSubjectValue.Of(Feed),
        ConfigChangedKind.Changed => FilterSubjectValue.Of(Changed),
        ConfigChangedKind.ChangedCount => FilterSubjectValue.Of((double)Changed.Count),
        ConfigChangedKind.IdCount => FilterSubjectValue.Of((double)(Added.Count + Removed.Count)),
        _ => FilterSubjectValue.Absent
    };

    public string FeedLabel => ConfigFeeds.LabelOf(Feed);

    public IReadOnlyList<string> Changed => Change?.Changed ?? [];
    public IReadOnlyList<string> Added => Change?.Added ?? [];
    public IReadOnlyList<string> Removed => Change?.Removed ?? [];

    public string Summary => Changed.Count == 0
        ? $"{FeedLabel} changed"
        : $"{FeedLabel}: {string.Join(", ", Changed)}";

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["feed"] = Feed,
        ["feedLabel"] = FeedLabel,
        ["sha"] = Sha,
        ["pageUrl"] = PageUrl,
        ["changed"] = FeedText.Joined(Changed),
        ["added"] = FeedText.Joined(Added),
        ["removed"] = FeedText.Joined(Removed)
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Response", FeedLabel, true),
            new("Hash", FeedText.Short(Sha), true)
        };
        if (Changed.Count > 0) fields.Add(new FeedEmbedField("Changed", FeedText.Listed(Changed), false));
        if (Added.Count > 0) fields.Add(new FeedEmbedField("Added", FeedText.Listed(Added), false));
        if (Removed.Count > 0) fields.Add(new FeedEmbedField("Removed", FeedText.Listed(Removed), false));
        return new FeedEmbed($"Egg, Inc. {FeedLabel} changed", PageUrl,
            Changed.Count == 0 ? 0x5aa9e6 : 0x8b5cf6, fields);
    }
}

public static class ConfigChangedKind {
    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";

    public static string PageUrl(string? baseUrl, string feed) {
        string root = string.IsNullOrEmpty(baseUrl) ? FeedDispatcher.DefaultPageBaseUrl : baseUrl.TrimEnd('/');
        return string.Equals(feed, ConfigFeeds.Periodicals, StringComparison.Ordinal)
            ? $"{root}/periodicals"
            : $"{root}/data";
    }

    public const string Feed = "feed";
    public const string Changed = "changed";
    public const string ChangedCount = "changed_count";
    public const string IdCount = "id_count";

    private static readonly string[] Aspects = [
        "sales", "events", "liveConfig", "mail", "contracts", "colleggtibles", "admin", "dlcItems", "shells",
        "shellSets", "decorators", "shellObjects", "shellGroups", "fontPacks", "artifacts", "missions", "crafting",
        "seasons"
    ];

    public static readonly FilterSet Defaults = FeedFilter.Rows([FeedFilter.Above(ChangedCount, 0)]);

    public static FilterSet Legacy(FeedSubscription sub) {
        var rows = new List<FilterCondition[]>();
        if (ConfigFeeds.ById(sub.Trigger) is { } feed) rows.Add([FeedFilter.Is(Feed, feed.Id)]);
        if (sub.Filters.Contains(FeedEventKinds.FilterRequireAspects)) rows.Add([FeedFilter.Above(ChangedCount, 0)]);
        if (sub.Filters.Contains(FeedEventKinds.FilterRequireIds)) rows.Add([FeedFilter.Above(IdCount, 0)]);
        return FeedFilter.Rows(rows);
    }

    private static ConfigChangedEvent Sample(string feed, ConfigChangeSummary? change) =>
        new(feed, SampleSha, PageUrl(null, feed), change);

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ConfigChanged, "Config changed",
        "A periodicals, game config, artifacts config or season response changed on the live server.",
        [
            FeedFilter.Select(Feed, "Response", ConfigFeeds.All.Select(f => new FilterOption(f.Id, f.Label))),
            FeedFilter.Set(Changed, "Changed aspect", FeedFilter.Options(Aspects)),
            FeedFilter.Number(ChangedCount, "Changed aspect count"),
            FeedFilter.Number(IdCount, "Added or removed entry count")
        ],
        Defaults,
        Legacy,
        ["feed", "feedLabel", "sha", "pageUrl", "changed", "added", "removed"],
        [
            new FeedSample("periodicals", "Periodicals: new contract and event",
                Sample(ConfigFeeds.Periodicals, new ConfigChangeSummary(
                    ["events", "contracts"],
                    ["contract:hab-rush-2026", "event:egg-boost"],
                    ["contract:winter-warmup-2026"]))),
            new FeedSample("dlc", "Config: new shell set",
                Sample(ConfigFeeds.Config, new ConfigChangeSummary(
                    ["shellSets", "shellObjects"],
                    ["shellSet:glacier", "shellObject:glacier_silo"],
                    []))),
            new FeedSample("afx", "Artifacts config: values changed",
                Sample(ConfigFeeds.Afx, new ConfigChangeSummary(["artifacts"], [], []))),
            new FeedSample("seasons", "Season infos: new season",
                Sample(ConfigFeeds.Seasons, new ConfigChangeSummary(["seasons"], ["season:fall-2026"], []))),
            new FeedSample("bare", "Response changed, nothing identified",
                Sample(ConfigFeeds.Periodicals, null))
        ]);
}
