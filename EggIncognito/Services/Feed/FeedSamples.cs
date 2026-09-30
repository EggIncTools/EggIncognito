using System.Collections.Frozen;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Feed;

public sealed record FeedSample(string Key, string Label, INotificationEvent Event);

public static class FeedSamples {
    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl;

    private static ProtoBuildEvent Proto(
        string platform, string appVersion, string build, string? clientVersion, string sha,
        bool created, bool protoChanged, VersionDelta delta, string? prevAppVersion, string? prevBuild,
        bool hasProto) =>
        new(0, platform, appVersion, build, clientVersion, sha, created, protoChanged,
            FeedDispatcher.BuildPageUrl(SampleUrl, platform, build), delta, prevAppVersion, prevBuild,
            ProtoVersionQuality.Flaws(platform, build, clientVersion, sha, hasProto));

    private static readonly IReadOnlyList<FeedSample> ProtoSamples = [
        new("forward", "New version",
            Proto("android", "1.37.0", "111358", "72", SampleSha, true, true,
                VersionDelta.Forward, "1.36.4", "111357", true)),
        new("proto_only", "Same version, proto changed",
            Proto("android", "1.37.0", "111358", "72", SampleSha, false, true,
                VersionDelta.Repeat, "1.37.0", "111358", true)),
        new("backfill", "Older build registered",
            Proto("android", "1.36.0", "111350", "71", SampleSha, true, true,
                VersionDelta.Backfill, "1.37.0", "111358", true)),
        new("broken", "Failed extraction",
            Proto("ios", "1.37.1", "111340", null, "", true, true,
                VersionDelta.Forward, "1.37.0", "1.37.0.1", false))
    ];

    private static ConfigChangedEvent Config(string feed, ConfigChangeSummary? change) =>
        new(feed, SampleSha, ConfigChangeNotifier.PageUrl(SampleUrl, feed), change);

    private static readonly IReadOnlyList<FeedSample> ConfigSamples = [
        new("periodicals", "Periodicals: new contract and event",
            Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(
                ["events", "contracts"],
                ["contract:hab-rush-2026", "event:egg-boost"],
                ["contract:winter-warmup-2026"]))),
        new("dlc", "Config: new shell set",
            Config(ConfigFeeds.Config, new ConfigChangeSummary(
                ["shellSets", "shellObjects"],
                ["shellSet:glacier", "shellObject:glacier_silo"],
                []))),
        new("afx", "Artifacts config: values changed",
            Config(ConfigFeeds.Afx, new ConfigChangeSummary(["artifacts"], [], []))),
        new("seasons", "Season infos: new season",
            Config(ConfigFeeds.Seasons, new ConfigChangeSummary(
                ["seasons"], ["season:fall-2026"], []))),
        new("bare", "Response changed, nothing identified",
            Config(ConfigFeeds.Periodicals, null))
    ];

    private static readonly IReadOnlyList<FeedSample> GameDataSamples = [
        new("binary_up", "New binary, documents rebuilt",
            new GameDataRebuiltEvent("1.37.0", "1.36.4", "android", SampleSha,
                ["eggs", "research", "habs", "missions"], $"{SampleUrl}/protos")),
        new("same_binary", "Same binary, one document changed",
            new GameDataRebuiltEvent("1.37.0", "1.37.0", "ios", SampleSha,
                ["boost-catalog"], $"{SampleUrl}/protos"))
    ];

    private static readonly FrozenDictionary<string, IReadOnlyList<FeedSample>> ByKind =
        new Dictionary<string, IReadOnlyList<FeedSample>>(StringComparer.Ordinal) {
            [FeedEventKinds.ProtoBuild] = ProtoSamples,
            [FeedEventKinds.ConfigChanged] = ConfigSamples,
            [FeedEventKinds.GameDataRebuilt] = GameDataSamples
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyList<FeedSample> For(string? eventKind) =>
        eventKind is not null && ByKind.TryGetValue(eventKind, out var samples) ? samples : [];

    public static FeedSample? Find(string? eventKind, string? key) =>
        For(eventKind).FirstOrDefault(s => s.Key == key);
}
