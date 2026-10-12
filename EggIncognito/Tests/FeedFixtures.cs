using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using Ei;

namespace EggIncognito.Tests;

public static class FeedFixtures {
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 16, 0, 0, TimeSpan.Zero);

    public static ProtoBuildEvent Proto(string platform, string app, string build, string? client, string sha,
        bool created, bool changed, VersionDelta delta, bool hasProto = true) =>
        new(1, platform, app, build, client, sha, created, changed, "https://x/protos", delta, "prev-app", "prev-build",
            ProtoVersionQuality.Flaws(platform, build, client, sha, hasProto));

    public static ProtoBuildEvent ProtoForward() => Proto("android", "2.0.0", "200000", "80", "sha-a", true, true, VersionDelta.Forward);

    public static ProtoBuildEvent ProtoRepeat() => Proto("android", "2.0.0", "200000", "80", "sha-a", false, true, VersionDelta.Repeat);

    public static ProtoBuildEvent ProtoBackfill() => Proto("android", "1.0.0", "100000", "70", "sha-b", true, true, VersionDelta.Backfill);

    public static ProtoBuildEvent ProtoBroken() => Proto("ios", "2.0.0", "200000", null, "", true, true, VersionDelta.Forward, false);

    public static ConfigChangedEvent Config(string feed, ConfigChangeSummary? change, params NewColleggtible[] eggs) =>
        new(feed, "sha", "https://x/periodicals", change, null, eggs);

    public static NewColleggtible Colleggtible(string id, params GameModifier.Types.GameDimension[] buffs) =>
        new(id, id, [.. buffs.Select(b => new ColleggtibleBuff(b, 1.1))]);

    public static GameDataRebuiltEvent GameData(string version, string? prev, params string[] docs) =>
        new(version, prev, "android", $"input-{version}-{docs.Length}", docs, "https://x/data");

    public static GameEventAddedEvent GameEvent(string id, bool ultra, string source, TimeSpan age) =>
        new(id, "type-a", "message", 2, ultra, Start, Start.AddHours(24), source, Start + age, "https://x/events");

    public static ContractReleasedEvent Contract(string id, Egg egg, bool leggacy, bool ultra, string source, TimeSpan age) =>
        new(id, id, (int)egg, null, Start, Start.AddDays(7), 7 * 86400d, leggacy, ultra, 1, 10, source, Start + age,
            "https://x/contracts");

    public static ContractReleasedEvent ContractLive(string id, Egg egg, bool leggacy = false, bool ultra = false) =>
        Contract(id, egg, leggacy, ultra, ContractSources.Device, TimeSpan.FromMinutes(5));

    public static GameEventAddedEvent GameEventLive(string id, bool ultra = false) =>
        GameEvent(id, ultra, GameEventSources.Device, TimeSpan.FromMinutes(5));

    public static IReadOnlyList<INotificationEvent> All() => [
        ProtoForward(), ProtoRepeat(), ProtoBackfill(), ProtoBroken(),
        Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["events"], ["event:a"], [])),
        Config(ConfigFeeds.Periodicals, null),
        Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["colleggtibles"], ["colleggtible:a"], []),
            Colleggtible("a", GameModifier.Types.GameDimension.ShippingCapacity)),
        GameData("2.0.0", "1.0.0", "eggs"),
        GameEventLive("e1"), GameEvent("e2", false, GameEventSources.Carpet, TimeSpan.FromDays(400)),
        ContractLive("c1", Egg.Fusion), ContractLive("c2", Egg.Tachyon, leggacy: true)
    ];

    public static IReadOnlyList<INotificationEvent> Of(string kind) => [.. All().Where(e => e.Kind == kind)];
}
