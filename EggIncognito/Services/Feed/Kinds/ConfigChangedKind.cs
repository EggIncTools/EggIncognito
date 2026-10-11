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
    public string? Platform => null;

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

    private static ConfigChangedEvent Sample(string feed, ConfigChangeSummary? change) =>
        new(feed, SampleSha, PageUrl(null, feed), change);

    private static FeedTriggerOption FeedTrigger(ConfigFeed feed) =>
        new(feed.Id, feed.Label, e => e is ConfigChangedEvent c && string.Equals(c.Feed, feed.Id, StringComparison.Ordinal));

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ConfigChanged, "Config changed",
        "A periodicals, game config, artifacts config or season response changed on the live server.",
        [
            new FeedTriggerOption(FeedEventKinds.TriggerAnyFeed, "Any response", e => e is ConfigChangedEvent),
            .. ConfigFeeds.All.Select(FeedTrigger)
        ],
        FeedEventKinds.TriggerAnyFeed,
        [
            new FeedFilterOption(FeedEventKinds.FilterRequireAspects, "Require identified change", true,
                e => e is ConfigChangedEvent { Changed.Count: 0 }),
            new FeedFilterOption(FeedEventKinds.FilterRequireIds, "Require added or removed entries", false,
                e => e is ConfigChangedEvent { Added.Count: 0, Removed.Count: 0 })
        ],
        ["feed", "feedLabel", "sha", "pageUrl", "changed", "added", "removed"],
        false,
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
        ],
        []);
}
